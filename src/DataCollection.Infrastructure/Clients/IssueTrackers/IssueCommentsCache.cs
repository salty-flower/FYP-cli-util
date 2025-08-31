using System.Collections.Concurrent;
using DataCollection.Infrastructure.Options;
using GitHub.Models;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

public class IssueCommentsCache(IOptions<PathsOptions> pathsOptions) : IIssueCommentsCache
{
    private readonly ConcurrentDictionary<string, List<IssueComment>?> commentsCache = [];
    private readonly string issueProfileDir = pathsOptions.Value.IssueProfileDir;

    public async Task<List<IssueComment>?> TryGetAsync(
        string owner,
        string repoName,
        long issueNumber
    )
    {
        var cacheKey = $"{owner}/{repoName}#{issueNumber}:comments";
        if (commentsCache.TryGetValue(cacheKey, out var cachedComments))
        {
            return cachedComments;
        }

        var commentsFile = GetCommentsFilePath(owner, repoName, issueNumber);
        if (!File.Exists(commentsFile))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(commentsFile);
        var comments = JsonConvert.DeserializeObject<List<IssueComment>?>(json);

        commentsCache[cacheKey] = comments;
        return comments;
    }

    public async Task SetAsync(
        string owner,
        string repoName,
        long issueNumber,
        List<IssueComment>? comments
    )
    {
        var cacheKey = $"{owner}/{repoName}#{issueNumber}:comments";
        commentsCache[cacheKey] = comments;

        var issueDir = Path.Combine(issueProfileDir, owner, repoName, issueNumber.ToString());
        if (!Directory.Exists(issueDir))
        {
            Directory.CreateDirectory(issueDir);
        }

        var commentsFile = GetCommentsFilePath(owner, repoName, issueNumber);
        await File.WriteAllTextAsync(
            commentsFile,
            JsonConvert.SerializeObject(comments, Formatting.Indented)
        );
    }

    private string GetCommentsFilePath(string owner, string repoName, long issueNumber) =>
        Path.Combine(issueProfileDir, owner, repoName, issueNumber.ToString(), "comments.json");
}
