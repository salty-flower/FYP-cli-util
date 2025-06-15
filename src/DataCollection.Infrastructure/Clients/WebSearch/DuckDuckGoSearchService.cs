using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using System.Web;
using DataCollection.Common;
using DataCollection.Common.Extensions;
using DataCollection.Infrastructure.Models.WebSearch;
using DataCollection.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;

namespace DataCollection.Infrastructure.Clients.WebSearch;

public partial class DuckDuckGoSearchService(
    IHttpClientFactory httpClientFac,
    ILogger<DuckDuckGoSearchService> logger
) : IWebSearchService
{
    private const int RateLimitPerMinute = 10;
    private const int RateLimitQueueSize = 5;
    private const int DefaultMaxResults = 10;
    private const string SearchUrl = "https://links.duckduckgo.com/d.js";

    private static readonly FixedWindowRateLimiter RateLimiter = new(
        new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = RateLimitPerMinute,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = RateLimitQueueSize,
        }
    );

    private static readonly Regex SearchRegex = SearchPattern();

    private static readonly string[] VqdPatterns =
    [
        """vqd['"]?\s*[:=]\s*['"]([^'"]+)['"]""",
        @"vqd['""]?\s*[:=]\s*([0-9\-]+)",
        """['"]([\d-]+)['"]\s*[;,]?\s*window\.vqd""",
        """window\.vqd\s*=\s*['"]([^'"]+)['"]""",
        """DDG\.duckbar\.vqd\s*=\s*['"]([^'"]+)['"]""",
    ];

    private static Dictionary<string, string> BuildQueryParameters(string query, string vqd) =>
        new()
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

    [LogEntryAndExit]
    public async Task<List<WebSearchResult>> SearchAsync(
        string query,
        int maxResults = DefaultMaxResults,
        CancellationToken cancellationToken = default
    )
    {
        await ApplyRateLimit(cancellationToken);

        var vqd = await ExtractVqdToken(query, cancellationToken);
        if (string.IsNullOrEmpty(vqd))
        {
            return [];
        }

        var searchResults = await PerformSearch(query, vqd, maxResults, cancellationToken);

        return searchResults;
    }

    private static async Task ApplyRateLimit(CancellationToken cancellationToken)
    {
        using var lease = await RateLimiter.AcquireAsync(1, cancellationToken);
        if (!lease.IsAcquired)
            throw new InvalidOperationException(
                "Rate limit exceeded - too many requests to DuckDuckGo"
            );
    }

    private async Task<string?> ExtractVqdToken(string query, CancellationToken cancellationToken)
    {
        var response = await httpClientFac
            .CreateClient("ddg")
            .GetAsync($"/?q={Uri.EscapeDataString(query)}&t=h_&ia=web", cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        ValidateResponseContent(content);
        return ExtractVqdFromContent(content);
    }

    private static void ValidateResponseContent(string content)
    {
        if (content.Contains("DDG.deep.is506"))
            throw new Exception("DuckDuckGo server error occurred");

        if (content.Contains("DDG.deep.anomalyDetectionBlock"))
            throw new Exception("DuckDuckGo detected anomaly - requests may be too frequent");
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

        var isSuccess = false;
        HttpResponseMessage? response = null;

        while (!isSuccess)
        {
            response = await httpClientFac
                .CreateClient("ddg")
                .GetAsync(searchUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            isSuccess =
                response.StatusCode != System.Net.HttpStatusCode.NoContent
                && response.StatusCode != System.Net.HttpStatusCode.Accepted;
            if (isSuccess)
                continue;
            // sleep
            logger.LogWarning("DuckDuckGo returned no content, retrying in 3 seconds...");
            await Task.Delay(3000, cancellationToken);
        }

        var content = await response!.Content.ReadAsStringAsync(cancellationToken);
        return ParseSearchResults(content, maxResults);
    }

    private static string BuildSearchUrl(Dictionary<string, string> queryParams) =>
        $"{SearchUrl}?{'&'.Join(
            queryParams.Select(kvp => $"{kvp.Key}={HttpUtility.UrlEncode(kvp.Value)}")
        )}";

    private string? ExtractVqdFromContent(string content)
    {
        foreach (var pattern in VqdPatterns)
        {
            var vqdMatch = Regex.Match(content, pattern, RegexOptions.IgnoreCase);
            if (!vqdMatch.Success)
                continue;
            var vqd = vqdMatch.Groups[1].Value;
            return vqd;
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

        var match = SearchRegex.Match(content);
        if (!match.Success)
        {
            logger.LogWarning("Could not find search results in DuckDuckGo response");
            return results;
        }

        var jsonData = match.Groups[1].Value.Replace("\t", "    ");
        var searchResults = JsonSerializer.Deserialize(
            jsonData,
            AppJsonContext.Default.JsonElementArray
        );

        return ProcessSearchResults(searchResults, maxResults);
    }

    private static List<WebSearchResult> ProcessSearchResults(
        JsonElement[]? searchResults,
        int maxResults
    ) =>
        searchResults == null
            ? []
            : searchResults
                .Take(maxResults)
                .Select(CreateWebSearchResult)
                .Where(r => r != null)
                .Cast<WebSearchResult>()
                .ToList();

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
            return false;

        url = urlElement.GetString() ?? "";
        title = titleElement.GetString() ?? "";
        description = descElement.GetString();

        return !string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(title);
    }

    private static WebSearchResult? CreateWebSearchResult(JsonElement result)
    {
        if (!TryExtractResultProperties(result, out var url, out var title, out var description))
            return null;

        if (IsAdvertisement(url))
            return null;

        return new WebSearchResult
        {
            Title = System.Net.WebUtility.HtmlDecode(title),
            Url = url,
            Snippet = System.Net.WebUtility.HtmlDecode(description ?? ""),
            Source = "DuckDuckGo",
        };
    }

    private static bool IsAdvertisement(string url) =>
        url.Contains("duckduckgo.com/y.js") || url.Contains("/y.js");

    [GeneratedRegex(
        @"DDG\.pageLayout\.load\('d',(\[.+\])\);DDG\.duckbar\.load(?:Module)?\('",
        RegexOptions.Compiled
    )]
    private static partial Regex SearchPattern();
}
