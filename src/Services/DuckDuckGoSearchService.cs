using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using System.Web;
using DataCollection.Models;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace DataCollection.Services;

/// <summary>
/// DuckDuckGo search service implementation using direct web scraping
/// </summary>
public class DuckDuckGoSearchService(HttpClient httpClient, ILogger<DuckDuckGoSearchService> logger)
    : IWebSearchService
{
    private const int RateLimitPerMinute = 10;
    private const int RateLimitQueueSize = 5;
    private const int DefaultMaxResults = 10;
    private const string BaseUrl = "https://duckduckgo.com";
    private const string SearchUrl = "https://links.duckduckgo.com/d.js";
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/115.0";

    private static readonly Dictionary<string, string> DefaultHeaders = new()
    {
        ["User-Agent"] = UserAgent,
        ["Accept"] = "application/json, text/javascript, */*; q=0.01",
        ["Accept-Language"] = "en-US,en;q=0.5",
        ["Referer"] = BaseUrl,
        ["Connection"] = "keep-alive",
    };

    private static readonly FixedWindowRateLimiter _rateLimiter = new(
        new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = RateLimitPerMinute,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = RateLimitQueueSize,
        }
    );

    private static readonly Regex SearchRegex = new(
        @"DDG\.pageLayout\.load\('d',(\[.+\])\);DDG\.duckbar\.load(?:Module)?\('",
        RegexOptions.Compiled
    );

    private static readonly string[] VqdPatterns =
    [
        @"vqd['""]?\s*[:=]\s*['""]([^'""]+)['""]",
        @"vqd['""]?\s*[:=]\s*([0-9\-]+)",
        @"['""]([\d-]+)['""]\s*[;,]?\s*window\.vqd",
        @"window\.vqd\s*=\s*['""]([^'""]+)['""]",
        @"DDG\.duckbar\.vqd\s*=\s*['""]([^'""]+)['""]",
    ];

    public async Task<List<WebSearchResult>> SearchAsync(
        string query,
        int maxResults = DefaultMaxResults,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            await ApplyRateLimit(cancellationToken);

            logger.LogInformation("Searching DuckDuckGo for: {Query}", query);

            var vqd = await ExtractVqdToken(query, cancellationToken);
            if (string.IsNullOrEmpty(vqd))
            {
                logger.LogWarning("Failed to extract VQD token for query: {Query}", query);
                return new List<WebSearchResult>();
            }

            var searchResults = await PerformSearch(query, vqd, maxResults, cancellationToken);

            logger.LogInformation(
                "Found {Count} results for query: {Query}",
                searchResults.Count,
                query
            );
            return searchResults;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error searching DuckDuckGo for query: {Query}", query);
            return new List<WebSearchResult>();
        }
    }

    private async Task ApplyRateLimit(CancellationToken cancellationToken)
    {
        using var lease = await _rateLimiter.AcquireAsync(1, cancellationToken);
        if (!lease.IsAcquired)
        {
            logger.LogWarning(
                "Rate limit exceeded for DuckDuckGo search, request queued or rejected"
            );
            throw new InvalidOperationException(
                "Rate limit exceeded - too many requests to DuckDuckGo"
            );
        }
    }

    private async Task<string?> ExtractVqdToken(string query, CancellationToken cancellationToken)
    {
        var searchUrl = $"{BaseUrl}/?q={Uri.EscapeDataString(query)}&t=h_&ia=web";
        logger.LogInformation("GET {Url}", searchUrl);

        var response = await httpClient.GetAsync(searchUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        ValidateResponseContent(content);

        return ExtractVqdFromContent(content);
    }

    private static void ValidateResponseContent(string content)
    {
        if (content.Contains("DDG.deep.is506"))
        {
            throw new Exception("DuckDuckGo server error occurred");
        }

        if (content.Contains("DDG.deep.anomalyDetectionBlock"))
        {
            throw new Exception("DuckDuckGo detected anomaly - requests may be too frequent");
        }
    }

    private async Task<List<WebSearchResult>> PerformSearch(
        string query,
        string vqd,
        int maxResults,
        CancellationToken cancellationToken
    )
    {
        var queryParams = BuildQueryParameters(query, vqd);
        var searchUrl = BuildSearchUrl(queryParams);

        var response = await httpClient.GetAsync(searchUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseSearchResults(content, maxResults);
    }

    private static Dictionary<string, string> BuildQueryParameters(string query, string vqd)
    {
        return new Dictionary<string, string>
        {
            ["q"] = query,
            ["t"] = "D",
            ["l"] = "en-us",
            ["kl"] = "wt-wt",
            ["s"] = "0",
            ["dl"] = "en",
            ["ct"] = "US",
            ["bing_market"] = "en-US",
            ["df"] = "",
            ["vqd"] = vqd,
            ["ex"] = "-1",
            ["sp"] = "1",
            ["bpa"] = "1",
            ["biaexp"] = "b",
            ["msvrtexp"] = "b",
            ["nadse"] = "b",
            ["eclsexp"] = "b",
            ["tjsexp"] = "b",
        };
    }

    private static string BuildSearchUrl(Dictionary<string, string> queryParams)
    {
        var queryString = string.Join(
            "&",
            queryParams.Select(kvp => $"{kvp.Key}={HttpUtility.UrlEncode(kvp.Value)}")
        );
        return $"{SearchUrl}?{queryString}";
    }

    private string? ExtractVqdFromContent(string content)
    {
        foreach (var pattern in VqdPatterns)
        {
            var vqdMatch = Regex.Match(content, pattern, RegexOptions.IgnoreCase);
            if (vqdMatch.Success)
            {
                var vqd = vqdMatch.Groups[1].Value;
                logger.LogDebug("Extracted VQD using pattern '{Pattern}': {VQD}", pattern, vqd);
                return vqd;
            }
        }

        LogVqdExtractionFailure(content);
        return null;
    }

    private void LogVqdExtractionFailure(string content)
    {
        if (!logger.IsEnabled(LogLevel.Debug))
            return;

        var sample = content.Length > 1000 ? content.Substring(0, 1000) : content;
        logger.LogDebug("Sample of DuckDuckGo response (first 1000 chars): {Sample}", sample);

        var vqdIndex = content.IndexOf("vqd", StringComparison.OrdinalIgnoreCase);
        if (vqdIndex >= 0)
        {
            var start = Math.Max(0, vqdIndex - 50);
            var length = Math.Min(100, content.Length - start);
            var context = content.Substring(start, length);
            logger.LogDebug("Found 'vqd' in context: {Context}", context);
        }

        logger.LogWarning("Could not extract VQD from DuckDuckGo response");
    }

    private List<WebSearchResult> ParseSearchResults(string content, int maxResults)
    {
        var results = new List<WebSearchResult>();

        try
        {
            var match = SearchRegex.Match(content);
            if (!match.Success)
            {
                logger.LogWarning("Could not find search results in DuckDuckGo response");
                return results;
            }

            var jsonData = match.Groups[1].Value.Replace("\t", "    ");
            var searchResults = JsonSerializer.Deserialize<JsonElement[]>(jsonData);

            return ProcessSearchResults(searchResults, maxResults);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing DuckDuckGo search results");
            return results;
        }
    }

    private static List<WebSearchResult> ProcessSearchResults(
        JsonElement[] searchResults,
        int maxResults
    )
    {
        var results = new List<WebSearchResult>();

        foreach (var result in searchResults.Take(maxResults))
        {
            var webResult = CreateWebSearchResult(result);
            if (webResult != null)
            {
                results.Add(webResult);
            }
        }

        return results;
    }

    private static WebSearchResult? CreateWebSearchResult(JsonElement result)
    {
        if (!TryExtractResultProperties(result, out var url, out var title, out var description))
        {
            return null;
        }

        if (IsAdvertisement(url))
        {
            return null;
        }

        return new WebSearchResult
        {
            Title = System.Net.WebUtility.HtmlDecode(title),
            Url = url,
            Snippet = System.Net.WebUtility.HtmlDecode(description ?? ""),
            Source = "DuckDuckGo",
        };
    }

    private static bool TryExtractResultProperties(
        JsonElement result,
        out string url,
        out string title,
        out string? description
    )
    {
        url = "";
        title = "";
        description = null;

        if (
            !result.TryGetProperty("u", out var urlElement)
            || !result.TryGetProperty("t", out var titleElement)
            || !result.TryGetProperty("a", out var descElement)
        )
        {
            return false;
        }

        url = urlElement.GetString() ?? "";
        title = titleElement.GetString() ?? "";
        description = descElement.GetString();

        return !string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(title);
    }

    private static bool IsAdvertisement(string url)
    {
        return url.Contains("duckduckgo.com/y.js") || url.Contains("/y.js");
    }
}
