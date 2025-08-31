using System.Collections.Concurrent;
using DataCollection.Infrastructure.Models.GitHub;
using DataCollection.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

public class IssueEventsCache(IOptions<PathsOptions> pathsOptions) : IIssueEventsCache
{
    private readonly ConcurrentDictionary<string, List<GitHubEvent>?> eventsCache = [];
    private readonly string issueProfileDir = pathsOptions.Value.IssueProfileDir;

    public async Task<List<GitHubEvent>?> TryGetAsync(
        string owner,
        string repoName,
        long issueNumber
    )
    {
        var cacheKey = $"{owner}/{repoName}#{issueNumber}:events";
        if (eventsCache.TryGetValue(cacheKey, out var cachedEvents))
        {
            return cachedEvents;
        }

        var eventsFile = GetEventsFilePath(owner, repoName, issueNumber);
        if (!File.Exists(eventsFile))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(eventsFile);
        var events = JsonConvert.DeserializeObject<List<GitHubEvent>?>(json);

        eventsCache[cacheKey] = events;
        return events;
    }

    public async Task SetAsync(
        string owner,
        string repoName,
        long issueNumber,
        List<GitHubEvent>? events
    )
    {
        var cacheKey = $"{owner}/{repoName}#{issueNumber}:events";
        eventsCache[cacheKey] = events;

        var issueDir = Path.Combine(issueProfileDir, owner, repoName, issueNumber.ToString());
        if (!Directory.Exists(issueDir))
        {
            Directory.CreateDirectory(issueDir);
        }

        var eventsFile = GetEventsFilePath(owner, repoName, issueNumber);
        await File.WriteAllTextAsync(
            eventsFile,
            JsonConvert.SerializeObject(events, Formatting.Indented)
        );
    }

    private string GetEventsFilePath(string owner, string repoName, long issueNumber) =>
        Path.Combine(issueProfileDir, owner, repoName, issueNumber.ToString(), "events.json");
}
