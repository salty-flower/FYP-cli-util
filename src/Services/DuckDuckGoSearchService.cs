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
    private static readonly Dictionary<string, string> Headers = new()
    {
        ["User-Agent"] =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/115.0",
        ["Accept"] = "application/json, text/javascript, */*; q=0.01",
        ["Accept-Language"] = "en-US,en;q=0.5",
        ["Referer"] = "https://duckduckgo.com/",
        ["Connection"] = "keep-alive",
    };

    private static readonly FixedWindowRateLimiter _rateLimiter = new(
        new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 10, // 10 requests per minute
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 5,
        }
    );

    private static readonly Regex SearchRegex = new(
        @"DDG\.pageLayout\.load\('d',(\[.+\])\);DDG\.duckbar\.load(?:Module)?\('",
        RegexOptions.Compiled
    );

    public async Task<List<WebSearchResult>> SearchAsync(
        string query,
        int maxResults = 10,
        CancellationToken cancellationToken = default
    )
    {
        var results = new List<WebSearchResult>();

        try
        {
            // Apply rate limiting
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

            logger.LogInformation("Searching DuckDuckGo for: {Query}", query);

            // Step 1: Get the initial search page to extract VQD token
            var searchUrl = $"https://duckduckgo.com/?q={Uri.EscapeDataString(query)}&t=h_&ia=web";
            logger.LogInformation("GET {Url}", searchUrl);

            var response = await httpClient.GetAsync(searchUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (content.Contains("DDG.deep.is506"))
            {
                throw new Exception("DuckDuckGo server error occurred");
            }

            if (content.Contains("DDG.deep.anomalyDetectionBlock"))
            {
                throw new Exception("DuckDuckGo detected anomaly - requests may be too frequent");
            }

            // Extract VQD token from the page content
            var vqd = ExtractVqdFromContent(content);
            if (string.IsNullOrEmpty(vqd))
            {
                logger.LogWarning(
                    "Failed to extract VQD token from initial page for query: {Query}",
                    query
                );
                return results;
            }

            logger.LogDebug("Extracted VQD token: {VQD}", vqd);

            var queryParams = new Dictionary<string, string>
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

            var queryString = string.Join(
                "&",
                queryParams.Select(kvp => $"{kvp.Key}={HttpUtility.UrlEncode(kvp.Value)}")
            );
            var searchUrl2 = $"https://links.duckduckgo.com/d.js?{queryString}";

            var response2 = await httpClient.GetAsync(searchUrl2, cancellationToken);
            response2.EnsureSuccessStatusCode();

            var content2 = await response2.Content.ReadAsStringAsync(cancellationToken);

            var parsedResults = ParseSearchResults(content2, maxResults);
            logger.LogInformation(
                "Found {Count} results for query: {Query}",
                parsedResults.Count,
                query
            );

            results.AddRange(parsedResults);

            return results;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error searching DuckDuckGo for query: {Query}", query);
            return results;
        }
    }

    private string ExtractVqdFromContent(string content)
    {
        // Try multiple VQD extraction patterns
        var vqdPatterns = new[]
        {
            @"vqd['""]?\s*[:=]\s*['""]([^'""]+)['""]",
            @"vqd['""]?\s*[:=]\s*([0-9\-]+)",
            @"['""]([\d-]+)['""]\s*[;,]?\s*window\.vqd",
            @"window\.vqd\s*=\s*['""]([^'""]+)['""]",
            @"DDG\.duckbar\.vqd\s*=\s*['""]([^'""]+)['""]",
        };

        foreach (var pattern in vqdPatterns)
        {
            var vqdMatch = Regex.Match(content, pattern, RegexOptions.IgnoreCase);
            if (vqdMatch.Success)
            {
                var vqd = vqdMatch.Groups[1].Value;
                logger.LogDebug("Extracted VQD using pattern '{Pattern}': {VQD}", pattern, vqd);
                return vqd;
            }
        }

        // Debug: log a sample of the content to understand what we're getting
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var sample = content.Length > 1000 ? content.Substring(0, 1000) : content;
            logger.LogDebug("Sample of DuckDuckGo response (first 1000 chars): {Sample}", sample);

            // Look for any occurrence of "vqd" in the content
            var vqdIndex = content.IndexOf("vqd", StringComparison.OrdinalIgnoreCase);
            if (vqdIndex >= 0)
            {
                var start = Math.Max(0, vqdIndex - 50);
                var length = Math.Min(100, content.Length - start);
                var context = content.Substring(start, length);
                logger.LogDebug("Found 'vqd' in context: {Context}", context);
            }
        }

        logger.LogWarning("Could not extract VQD from DuckDuckGo response");
        return null;
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

            foreach (var result in searchResults.Take(maxResults))
            {
                if (
                    !result.TryGetProperty("u", out var urlElement)
                    || !result.TryGetProperty("t", out var titleElement)
                    || !result.TryGetProperty("a", out var descElement)
                )
                {
                    continue;
                }

                var url = urlElement.GetString();
                var title = titleElement.GetString();
                var description = descElement.GetString();

                if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(title))
                {
                    continue;
                }

                // Skip ads
                if (url.Contains("duckduckgo.com/y.js") || url.Contains("/y.js"))
                {
                    continue;
                }

                results.Add(
                    new WebSearchResult
                    {
                        Title = System.Net.WebUtility.HtmlDecode(title),
                        Url = url,
                        Snippet = System.Net.WebUtility.HtmlDecode(description ?? ""),
                        Source = "DuckDuckGo",
                    }
                );
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing DuckDuckGo search results");
        }

        return results;
    }
}
