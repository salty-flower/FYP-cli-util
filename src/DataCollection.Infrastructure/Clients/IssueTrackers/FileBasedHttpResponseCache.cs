using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataCollection.Infrastructure.Models.GitHub;
using DataCollection.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

public partial class FileBasedHttpResponseCache(
    IOptionsMonitor<PathsOptions> pathsOptions,
    IOptionsMonitor<HttpCacheOptions> cacheOptions,
    ILogger<FileBasedHttpResponseCache> logger
) : IHttpResponseCache
{
    [JsonSerializable(typeof(CacheMetadata))]
    [JsonSerializable(typeof(CachedResponseHeaders))]
    [JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
    private partial class CacheJsonContext : JsonSerializerContext;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> entryLocks = new();

    private readonly string cacheRoot = pathsOptions.CurrentValue.HttpCacheDir;

    public async Task<CachedHttpResponse?> GetAsync(
        HttpRequestMessage request,
        CancellationToken ct
    )
    {
        if (!cacheOptions.CurrentValue.Enabled || request.RequestUri is null)
            return null;

        var url = request.RequestUri.ToString();
        var cacheKey = GetCacheKey(request);
        var cacheDir = GetCacheDirectory(cacheKey);
        var metaPath = GetMetadataPath(cacheDir);
        var bodyPath = GetBodyPath(cacheDir);

        if (!File.Exists(metaPath) || !File.Exists(bodyPath))
            return null;

        var cacheLock = GetLock(cacheKey);
        await cacheLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var metaStream = File.OpenRead(metaPath);
            var metadata = await JsonSerializer
                .DeserializeAsync<CacheMetadata>(
                    metaStream,
                    CacheJsonContext.Default.CacheMetadata,
                    ct
                )
                .ConfigureAwait(false);

            if (metadata is null)
                return null;

            var body = await File.ReadAllTextAsync(bodyPath, ct).ConfigureAwait(false);
            return new CachedHttpResponse(
                metadata.Url,
                metadata.StatusCode,
                metadata.Headers,
                body,
                metadata.ETag,
                metadata.CachedAt,
                metadata.ExpiresAt
            );
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            logger.LogWarning(ex, "Failed to read cached response for {Url}", url);
            TryDeleteDirectory(cacheDir);
            return null;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    public async Task SetAsync(HttpResponseMessage response, TimeSpan? ttl, CancellationToken ct)
    {
        if (!cacheOptions.CurrentValue.Enabled || ttl is null)
            return;

        if (response.RequestMessage?.RequestUri is null)
            return;

        var request = response.RequestMessage;
        var url = request.RequestUri.ToString();
        var cacheKey = GetCacheKey(request);
        var cacheDir = GetCacheDirectory(cacheKey);
        var metaPath = GetMetadataPath(cacheDir);
        var bodyPath = GetBodyPath(cacheDir);
        var cacheLock = GetLock(cacheKey);

        await cacheLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(cacheDir);
            var body = response.Content is not null
                ? await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)
                : string.Empty;

            var bodySize = Encoding.UTF8.GetByteCount(body);
            if (cacheOptions.CurrentValue.MaxCacheSizeBytes > 0)
            {
                var currentSize = await GetCacheSizeAsync(ct).ConfigureAwait(false);
                if (currentSize + bodySize > cacheOptions.CurrentValue.MaxCacheSizeBytes)
                {
                    logger.LogDebug(
                        "Skipping cache write for {Url} because it would exceed max size {MaxSizeBytes} bytes",
                        url,
                        cacheOptions.CurrentValue.MaxCacheSizeBytes
                    );
                    return;
                }
            }

            await File.WriteAllTextAsync(bodyPath, body, ct).ConfigureAwait(false);

            var headers = CollectHeaders(response);
            var metadata = new CacheMetadata(
                url,
                (int)response.StatusCode,
                headers,
                response.Headers.ETag?.ToString(),
                DateTimeOffset.UtcNow,
                ttl.HasValue ? DateTimeOffset.UtcNow.Add(ttl.Value) : null,
                bodySize
            );

            await using var metaStream = File.Create(metaPath);
            await JsonSerializer
                .SerializeAsync(metaStream, metadata, CacheJsonContext.Default.CacheMetadata, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to cache response for {Url}", url);
        }
        finally
        {
            cacheLock.Release();
        }
    }

    public Task<bool> ExistsAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!cacheOptions.CurrentValue.Enabled || request.RequestUri is null)
            return Task.FromResult(false);

        var cacheKey = GetCacheKey(request);
        var cacheDir = GetCacheDirectory(cacheKey);
        var exists = File.Exists(GetMetadataPath(cacheDir)) && File.Exists(GetBodyPath(cacheDir));
        return Task.FromResult(exists);
    }

    public async Task InvalidateAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri is null)
            return;

        var cacheKey = GetCacheKey(request);
        var cacheDir = GetCacheDirectory(cacheKey);
        var cacheLock = GetLock(cacheKey);
        await cacheLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            TryDeleteDirectory(cacheDir);
        }
        finally
        {
            cacheLock.Release();
        }
    }

    public async Task<long> GetCacheSizeAsync(CancellationToken ct)
    {
        if (!cacheOptions.CurrentValue.Enabled || !Directory.Exists(cacheRoot))
            return 0L;

        long totalSize = 0;

        foreach (var directory in Directory.EnumerateDirectories(cacheRoot))
        {
            ct.ThrowIfCancellationRequested();
            var metaPath = GetMetadataPath(directory);
            if (!File.Exists(metaPath))
                continue;

            try
            {
                using var stream = File.OpenRead(metaPath);
                var metadata = await JsonSerializer.DeserializeAsync(
                    stream,
                    CacheJsonContext.Default.CacheMetadata,
                    ct
                );
                if (metadata != null)
                    totalSize += metadata.BodySizeBytes;
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                logger.LogDebug(ex, "Failed to read metadata from {MetaPath}", metaPath);
            }
        }

        return totalSize;
    }

    private static CachedResponseHeaders CollectHeaders(HttpResponseMessage response)
    {
        var responseHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in response.Headers)
            responseHeaders[header.Key] = header.Value.ToArray();

        var contentHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (response.Content is not null)
        {
            foreach (var header in response.Content.Headers)
                contentHeaders[header.Key] = header.Value.ToArray();
        }

        return new CachedResponseHeaders(responseHeaders, contentHeaders);
    }

    private static readonly string[] CacheKeyHeaderNames =
    {
        "Authorization",
        "Accept",
        "X-GitHub-Api-Version",
    };

    private static string GetCacheKey(HttpRequestMessage request)
    {
        var seed = BuildCacheKeySeed(request);
        return Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))
            .ToLowerInvariant();
    }

    private static string BuildCacheKeySeed(HttpRequestMessage request)
    {
        var builder = new StringBuilder();
        builder.AppendLine(request.RequestUri!.ToString());

        foreach (var headerName in CacheKeyHeaderNames)
        {
            builder.Append(headerName).Append(':');
            if (TryGetHeaderValues(request, headerName, out var values))
            {
                builder.AppendLine(string.Join(',', values));
            }
            else
            {
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private static bool TryGetHeaderValues(
        HttpRequestMessage request,
        string headerName,
        out IEnumerable<string> values
    )
    {
        if (request.Headers.TryGetValues(headerName, out var headerValues))
        {
            values = headerValues;
            return true;
        }

        if (request.Content?.Headers.TryGetValues(headerName, out var contentHeaderValues) ?? false)
        {
            values = contentHeaderValues;
            return true;
        }

        values = Array.Empty<string>();
        return false;
    }

    private string GetCacheDirectory(string cacheKey) => Path.Combine(cacheRoot, cacheKey);

    private static string GetMetadataPath(string cacheDir) => Path.Combine(cacheDir, "meta.json");

    private static string GetBodyPath(string cacheDir) => Path.Combine(cacheDir, "body");

    private SemaphoreSlim GetLock(string cacheKey) =>
        entryLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));

    private static void TryDeleteDirectory(string cacheDir)
    {
        try
        {
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
        catch (IOException)
        {
            // Ignore transient IO issues on delete
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore permission issues
        }
    }
}
