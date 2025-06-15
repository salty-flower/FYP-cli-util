using DataCollection.Infrastructure.Models.GitHub;
using GitHub.Models;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

public interface IGitHubClient
{
    Task<List<string>> SearchForRepositoryAsync(string keyword);
    Task<FullRepository> GetRepositoryInfoAsync(string owner, string repoName);
    Task<object> GetUserAsync(string userLogin);
    Task<bool> IsUserContributorAsync(string userLogin, FullRepository repository);
    Task<int> GetUserIssuesCountAsync(string userLogin, FullRepository repository);
    Task<(int total, int merged)> GetUserPullRequestsAsync(
        string userLogin,
        FullRepository repository
    );
    Task<Issue?> GetIssueAsync(string owner, string repoName, long issueNumber);
    Task<List<IssueComment>?> GetIssueCommentsAsync(
        string owner,
        string repoName,
        long issueNumber
    );
    Task<List<GitHubEvent>?> GetIssueEventsAsync(string owner, string repoName, long issueNumber);
    Task<string?> GetRepositoryReadmeAsync(string owner, string repoName);
    Task<string?> GetFileContentAsync(string owner, string repoName, string filePath);
    Task<RepositoryTree?> GetRepositoryTreeAsync(
        string owner,
        string repoName,
        bool recursive = true
    );
}
