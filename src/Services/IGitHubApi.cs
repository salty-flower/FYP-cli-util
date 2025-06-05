using System.Collections.Generic;
using System.Threading.Tasks;
using DataCollection.Models.GitHub;
using GitHub.Models;
using Refit;

namespace DataCollection.Services;

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

    /// <summary>
    /// Get repository file content
    /// </summary>
    [Get("/repos/{owner}/{repoName}/contents/{filePath}")]
    Task<GitHubFileContent?> GetRepositoryFileContentAsync(
        string owner,
        string repoName,
        string filePath
    );

    /// <summary>
    /// Make a generic GET request to the GitHub API and return raw JSON string
    /// </summary>
    [Get("/{endpoint}")]
    Task<string> GetJsonAsync(string endpoint);
}

/// <summary>
/// GitHub file content response model
/// </summary>
public class GitHubFileContent
{
    public string? Content { get; set; }
    public string? Encoding { get; set; }
    public string? Name { get; set; }
    public string? Path { get; set; }
}
