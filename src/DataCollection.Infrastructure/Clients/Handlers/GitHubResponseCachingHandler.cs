using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DataCollection.Infrastructure.Clients.IssueTrackers;
using DataCollection.Infrastructure.Models.GitHub;
using DataCollection.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Infrastructure.Clients.Handlers;

public class GitHubResponseCachingHandler : DelegatingHandler
{
    private static readonly TimeSpan SearchTtl = TimeSpan.FromDays(7);

    private readonly IHttpResponseCache cache;
    private readonly ILogger<GitHubResponseCachingHandler> logger;
    private readonly HttpCacheOptions cacheOptions;

    public GitHubResponseCachingHandler(
        IHttpResponseCache cache,
        IOptions<HttpCacheOptions> cacheOptions,
        ILogger<GitHubResponseCachingHandler> logger
    )
    {
        this.cache = cache;
        this.logger = logger;
        this.cacheOptions = cacheOptions.Value;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (!cacheOptions.Enabled || !ShouldCacheRequest(request) || request.RequestUri is null)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var requestUrl = request.RequestUri.ToString();
        var cacheDuration = await GetCacheDurationAsync(request.RequestUri.AbsolutePath)
            .ConfigureAwait(false);

        if (cacheDuration is null)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var cachedResponse = await cache.GetAsync(request, cancellationToken).ConfigureAwait(false);
        if (cachedResponse is not null && IsCacheEntryValid(cachedResponse))
        {
            logger.LogDebug("GitHub cache hit for {Url}", requestUrl);
            return CreateHttpResponseMessage(cachedResponse, request);
        }

        if (cachedResponse is not null && !string.IsNullOrEmpty(cachedResponse.ETag))
        {
            TrySetIfNoneMatchHeader(request, cachedResponse.ETag);
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotModified && cachedResponse is not null)
        {
            logger.LogDebug("GitHub cache revalidated for {Url}", requestUrl);
            var refreshedCacheEntry = await RefreshCacheFrom304Async(
                    cachedResponse,
                    response,
                    cacheDuration,
                    cancellationToken
                )
                .ConfigureAwait(false);

            response.Dispose();
            return CreateHttpResponseMessage(refreshedCacheEntry, request);
        }

        if (ShouldCacheResponse(response))
        {
            await CacheSuccessfulResponseAsync(response, cacheDuration, cancellationToken)
                .ConfigureAwait(false);
        }

        return response;
    }

    private static bool ShouldCacheRequest(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Get)
        {
            return false;
        }

        if (request.RequestUri is null)
        {
            return false;
        }

        return !request.RequestUri.AbsolutePath.Equals("/user", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldCacheResponse(HttpResponseMessage response) =>
        response.IsSuccessStatusCode;

    private static bool IsCacheEntryValid(CachedHttpResponse cachedResponse)
    {
        if (cachedResponse.ExpiresAt is null)
        {
            return true;
        }

        return cachedResponse.ExpiresAt > DateTimeOffset.UtcNow;
    }

    private async Task CacheSuccessfulResponseAsync(
        HttpResponseMessage response,
        TimeSpan? cacheDuration,
        CancellationToken cancellationToken
    )
    {
        if (cacheDuration is null)
        {
            return;
        }

        var originalContent = response.Content;
        var body = originalContent is not null
            ? await originalContent.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
            : string.Empty;

        var mediaType = originalContent?.Headers.ContentType?.MediaType ?? "application/json";
        var newContent = new StringContent(body, Encoding.UTF8, mediaType);

        if (originalContent is not null)
        {
            foreach (var header in originalContent.Headers)
            {
                if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                newContent.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        response.Content = newContent;
        await cache.SetAsync(response, cacheDuration, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CachedHttpResponse> RefreshCacheFrom304Async(
        CachedHttpResponse cachedResponse,
        HttpResponseMessage revalidationResponse,
        TimeSpan? cacheDuration,
        CancellationToken cancellationToken
    )
    {
        if (cacheDuration is null)
        {
            return cachedResponse;
        }

        var mergedHeaders = CloneHeaders(cachedResponse.Headers);

        foreach (var header in revalidationResponse.Headers)
        {
            mergedHeaders.Response[header.Key] = header.Value.ToArray();
        }

        if (revalidationResponse.Content is not null)
        {
            foreach (var header in revalidationResponse.Content.Headers)
            {
                mergedHeaders.Content[header.Key] = header.Value.ToArray();
            }
        }

        var mediaType = TryGetHeaderValue(mergedHeaders, "Content-Type") ?? "application/json";
        var etagString = revalidationResponse.Headers.ETag?.ToString() ?? cachedResponse.ETag;
        var requestMessage = revalidationResponse.RequestMessage;
        if (requestMessage is null)
        {
            logger.LogDebug(
                "Skipping cache refresh for {Url} because the revalidation response had no request",
                cachedResponse.Url
            );

            var cachedAtFallback = DateTimeOffset.UtcNow;
            var expiresAtFallback = cacheDuration.HasValue
                ? cachedAtFallback.Add(cacheDuration.Value)
                : cachedResponse.ExpiresAt;

            return new CachedHttpResponse(
                cachedResponse.Url,
                cachedResponse.StatusCode,
                mergedHeaders,
                cachedResponse.ResponseBody,
                etagString,
                cachedAtFallback,
                expiresAtFallback
            );
        }

        var refreshedResponse = new HttpResponseMessage((HttpStatusCode)cachedResponse.StatusCode)
        {
            RequestMessage = requestMessage,
            Content = new StringContent(cachedResponse.ResponseBody, Encoding.UTF8, mediaType),
        };

        foreach (var header in mergedHeaders.Response)
        {
            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            refreshedResponse.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var header in mergedHeaders.Content)
        {
            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            refreshedResponse.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (
            !string.IsNullOrEmpty(etagString)
            && EntityTagHeaderValue.TryParse(etagString, out var newEtag)
        )
        {
            refreshedResponse.Headers.ETag = newEtag;
        }

        await cache
            .SetAsync(refreshedResponse, cacheDuration, cancellationToken)
            .ConfigureAwait(false);

        var cachedAt = DateTimeOffset.UtcNow;
        var expiresAt = cacheDuration.HasValue
            ? cachedAt.Add(cacheDuration.Value)
            : cachedResponse.ExpiresAt;

        return new CachedHttpResponse(
            cachedResponse.Url,
            cachedResponse.StatusCode,
            mergedHeaders,
            cachedResponse.ResponseBody,
            etagString,
            cachedAt,
            expiresAt
        );
    }

    private async Task<TimeSpan?> GetCacheDurationAsync(string requestPath)
    {
        var ttl = GetCacheDuration(requestPath);
        return await Task.FromResult(ttl).ConfigureAwait(false);
    }

    private TimeSpan? GetCacheDuration(string requestPath)
    {
        if (requestPath.Equals("/user", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (requestPath.Contains("/commits/", StringComparison.OrdinalIgnoreCase))
        {
            return cacheOptions.CommitsTtl;
        }

        if (
            requestPath.Contains("/repos/", StringComparison.OrdinalIgnoreCase)
            && (
                requestPath.Contains("/issues/", StringComparison.OrdinalIgnoreCase)
                || requestPath.Contains("/events", StringComparison.OrdinalIgnoreCase)
                || requestPath.Contains("/comments", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            return cacheOptions.DefaultTtl;
        }

        if (requestPath.Contains("/search/", StringComparison.OrdinalIgnoreCase))
        {
            return SearchTtl;
        }

        return cacheOptions.DefaultTtl;
    }

    private static HttpResponseMessage CreateHttpResponseMessage(
        CachedHttpResponse cachedResponse,
        HttpRequestMessage request
    )
    {
        var mediaType =
            TryGetHeaderValue(cachedResponse.Headers, "Content-Type") ?? "application/json";
        var response = new HttpResponseMessage((HttpStatusCode)cachedResponse.StatusCode)
        {
            RequestMessage = request,
            Content = new StringContent(cachedResponse.ResponseBody, Encoding.UTF8, mediaType),
        };

        foreach (var header in cachedResponse.Headers.Response)
        {
            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            response.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var header in cachedResponse.Headers.Content)
        {
            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            response.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (
            !string.IsNullOrEmpty(cachedResponse.ETag)
            && EntityTagHeaderValue.TryParse(cachedResponse.ETag, out var etagValue)
        )
        {
            response.Headers.ETag = etagValue;
        }

        return response;
    }

    private static CachedResponseHeaders CloneHeaders(CachedResponseHeaders headers)
    {
        return new CachedResponseHeaders(
            new Dictionary<string, string[]>(headers.Response, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string[]>(headers.Content, StringComparer.OrdinalIgnoreCase)
        );
    }

    private static string? TryGetHeaderValue(CachedResponseHeaders headers, string headerName)
    {
        if (
            headers.Content.TryGetValue(headerName, out var contentValues)
            && contentValues.Length > 0
        )
        {
            return contentValues[0];
        }

        if (
            headers.Response.TryGetValue(headerName, out var responseValues)
            && responseValues.Length > 0
        )
        {
            return responseValues[0];
        }

        return null;
    }

    private static void TrySetIfNoneMatchHeader(HttpRequestMessage request, string etag)
    {
        if (EntityTagHeaderValue.TryParse(etag, out var entityTag))
        {
            request.Headers.IfNoneMatch.Clear();
            request.Headers.IfNoneMatch.Add(entityTag);
        }
        else
        {
            request.Headers.Remove("If-None-Match");
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }
    }
}
