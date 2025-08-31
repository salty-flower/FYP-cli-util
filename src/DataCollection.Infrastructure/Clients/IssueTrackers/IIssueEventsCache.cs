using DataCollection.Infrastructure.Models.GitHub;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

public interface IIssueEventsCache
{
    Task<List<GitHubEvent>?> TryGetAsync(string owner, string repoName, long issueNumber);
    Task SetAsync(string owner, string repoName, long issueNumber, List<GitHubEvent>? events);
}
