using DataCollection.Infrastructure.Models.GitHub;
using GitHub.Models;
using Refit;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

/// <summary>
/// Refit interface for GitHub API calls that were previously handled manually
/// </summary>
public interface IGitHubApi
{
    /// <summary>
    /// Fetch issue events to avoid SDK integer overflow bug
    /// https://github.com/octokit/dotnet-sdk/issues/117
    /// </summary>
    [Get("/repos/{owner}/{repoName}/issues/{issueNumber}/events")]
    Task<List<GitHubEvent>?> GetIssueEventsAsync(
        string owner,
        string repoName,
        long issueNumber,
        CancellationToken cancellationToken = default
    );

    [Get("/repos/{owner}/{repoName}/issues/{issueNumber}/timeline")]
    Task<List<GitHubTimelineEvent>?> GetIssueTimelineAsync(
        string owner,
        string repoName,
        long issueNumber,
        [AliasAs("per_page")] int perPage = 100,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Fetch issue comments to avoid SDK integer overflow bug
    /// </summary>
    [Get("/repos/{owner}/{repoName}/issues/{issueNumber}/comments")]
    Task<List<GitHubIssueComment>?> GetIssueCommentsAsync(
        string owner,
        string repoName,
        long issueNumber,
        CancellationToken cancellationToken = default
    );

    [Get("/repos/{owner}/{repoName}/contents/{filePath}")]
    Task<GitHubFileContent?> GetRepositoryFileContentAsync(
        string owner,
        string repoName,
        string filePath
    );

    [Get("/search/issues")]
    Task<string> SearchIssuesAsync([AliasAs("q")] string query);

    [Get("/repos/{owner}/{repoName}/git/trees/{tree_sha}")]
    Task<string> GetGitTreeAsync(
        string owner,
        string repoName,
        string tree_sha,
        [AliasAs("recursive")] int? recursive
    );

    /// <summary>
    /// Get issue with proper label deserialization
    /// </summary>
    [Get("/repos/{owner}/{repoName}/issues/{issueNumber}")]
    Task<GitHubIssue?> GetIssueAsync(
        string owner,
        string repoName,
        long issueNumber,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Get issue info to check for transfers/redirects
    /// </summary>
    [Get("/repos/{owner}/{repoName}/issues/{issueNumber}")]
    Task<string> GetIssueJsonAsync(string owner, string repoName, long issueNumber);

    /// <summary>
    /// Verify GitHub authentication status
    /// </summary>
    [Get("/user")]
    Task<string> GetUserAsync();

    /// <summary>
    /// Get issue by repository ID (alternative to owner/repo format) - strongly typed
    /// </summary>
    [Get("/repositories/{repositoryId}/issues/{issueNumber}")]
    Task<Issue?> GetIssueByRepositoryIdAsync(long repositoryId, long issueNumber);

    [Get("/repos/{owner}/{repoName}/commits/{commitSha}")]
    Task<GitHubCommit?> GetCommitAsync(
        string owner,
        string repoName,
        string commitSha,
        CancellationToken cancellationToken = default
    );

    [Get("/repos/{owner}/{repoName}/pulls/{pullNumber}")]
    Task<GitHubPullRequestDetails?> GetPullRequestAsync(
        string owner,
        string repoName,
        int pullNumber,
        CancellationToken cancellationToken = default
    );

    [Get("/repos/{owner}/{repoName}/pulls/{pullNumber}/files")]
    Task<List<GitHubPullRequestFile>?> GetPullRequestFilesAsync(
        string owner,
        string repoName,
        int pullNumber,
        CancellationToken cancellationToken = default
    );
}
