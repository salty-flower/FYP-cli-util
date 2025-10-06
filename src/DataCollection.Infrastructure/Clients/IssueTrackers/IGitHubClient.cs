using DataCollection.Infrastructure.Models.GitHub;
using GitHub.Models;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

public interface IGitHubClient
{
    Task<List<string>> SearchForRepositoryAsync(string keyword);
    Task<FullRepository> GetRepositoryInfoAsync(
        string owner,
        string repoName,
        CancellationToken cancellationToken = default
    );
    Task<object> GetUserAsync(string userLogin);
    Task<bool> IsUserContributorAsync(string userLogin, FullRepository repository);
    Task<int> GetUserIssuesCountAsync(string userLogin, FullRepository repository);
    Task<(int total, int merged)> GetUserPullRequestsAsync(
        string userLogin,
        FullRepository repository
    );
    Task<Issue?> GetIssueAsync(string owner, string repoName, long issueNumber);
    Task<GitHubIssue?> GetIssueWithLabelsAsync(
        string owner,
        string repoName,
        long issueNumber,
        CancellationToken cancellationToken = default
    );
    Task<List<IssueComment>?> GetIssueCommentsAsync(
        string owner,
        string repoName,
        long issueNumber,
        CancellationToken cancellationToken = default
    );
    Task<List<GitHubEvent>?> GetIssueEventsAsync(
        string owner,
        string repoName,
        long issueNumber,
        CancellationToken cancellationToken = default
    );
    Task<string?> GetRepositoryReadmeAsync(string owner, string repoName);
    Task<string?> GetFileContentAsync(string owner, string repoName, string filePath);
    Task<RepositoryTree?> GetRepositoryTreeAsync(
        string owner,
        string repoName,
        bool recursive = true
    );
    Task<GitHubCommit?> GetCommitAsync(
        string owner,
        string repoName,
        string commitSha,
        CancellationToken cancellationToken = default
    );
    Task<GitHubPullRequestDetails?> GetPullRequestAsync(
        string owner,
        string repoName,
        int pullNumber,
        CancellationToken cancellationToken = default
    );
    Task<IReadOnlyList<GitHubPullRequestFile>?> GetPullRequestFilesAsync(
        string owner,
        string repoName,
        int pullNumber,
        CancellationToken cancellationToken = default
    );
}
