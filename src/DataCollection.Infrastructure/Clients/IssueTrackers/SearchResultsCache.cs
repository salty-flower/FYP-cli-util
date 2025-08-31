using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using DataCollection.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

public record SearchResultEntry(string Query, int Count, DateTime CachedAt);

public class SearchResultsCache(IOptions<PathsOptions> pathsOptions) : ISearchResultsCache
{
    private readonly ConcurrentDictionary<string, int> searchCache = [];
    private readonly string issueTrackerDir = pathsOptions.Value.IssueTrackerDir;

    public async Task<int?> TryGetAsync(string searchQuery)
    {
        if (searchCache.TryGetValue(searchQuery, out var cachedCount))
        {
            return cachedCount;
        }

        var searchFile = GetSearchFilePath(searchQuery);
        if (!File.Exists(searchFile))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(searchFile);
        var entry = JsonConvert.DeserializeObject<SearchResultEntry>(json);

        if (entry == null)
        {
            return null;
        }

        // Cache results for 24 hours to balance freshness vs API usage
        if (DateTime.UtcNow - entry.CachedAt > TimeSpan.FromHours(24))
        {
            File.Delete(searchFile);
            return null;
        }

        searchCache[searchQuery] = entry.Count;
        return entry.Count;
    }

    public async Task SetAsync(string searchQuery, int count)
    {
        searchCache[searchQuery] = count;

        var searchDir = Path.Combine(issueTrackerDir, "search");
        if (!Directory.Exists(searchDir))
        {
            Directory.CreateDirectory(searchDir);
        }

        var entry = new SearchResultEntry(searchQuery, count, DateTime.UtcNow);
        var searchFile = GetSearchFilePath(searchQuery);
        await File.WriteAllTextAsync(
            searchFile,
            JsonConvert.SerializeObject(entry, Formatting.Indented)
        );
    }

    private string GetSearchFilePath(string searchQuery)
    {
        var queryHash = ComputeSha256Hash(searchQuery);
        return Path.Combine(issueTrackerDir, "search", $"{queryHash}.json");
    }

    private static string ComputeSha256Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
