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
    Task<List<GitHubEvent>?> GetIssueEventsAsync(string owner, string repoName, long issueNumber);

    /// <summary>
    /// Fetch issue comments to avoid SDK integer overflow bug
    /// </summary>
    [Get("/repos/{owner}/{repoName}/issues/{issueNumber}/comments")]
    Task<List<IssueComment>?> GetIssueCommentsAsync(
        string owner,
        string repoName,
        long issueNumber
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
    Task<GitHubIssue?> GetIssueAsync(string owner, string repoName, long issueNumber);

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
}
