using DataCollection.Infrastructure.Models.GitHub;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

public interface IIssueCommentsCache
{
    Task<List<GitHubIssueComment>?> TryGetAsync(string owner, string repoName, long issueNumber);
    Task SetAsync(
        string owner,
        string repoName,
        long issueNumber,
        List<GitHubIssueComment>? comments
    );
}
