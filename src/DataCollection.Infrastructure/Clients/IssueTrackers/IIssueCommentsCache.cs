using GitHub.Models;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

public interface IIssueCommentsCache
{
    Task<List<IssueComment>?> TryGetAsync(string owner, string repoName, long issueNumber);
    Task SetAsync(string owner, string repoName, long issueNumber, List<IssueComment>? comments);
}
