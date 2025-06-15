using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace DataCollection.Application.Common.Services;

public partial class UrlProcessingService
{
    private static readonly Regex GitHubUrlPattern = GitHubUrlRegex();

    public bool IsGitHubUrl(string url) => GitHubUrlPattern.IsMatch(url);

    public (string owner, string repo) ExtractGitHubOwnerAndRepo(string url)
    {
        var match = GitHubUrlPattern.Match(url);
        return match.Success
            ? (match.Groups[1].Value, match.Groups[2].Value.TrimEnd('/'))
            : (string.Empty, string.Empty);
    }

    public string NormalizeUrl(string url) => url.Trim().TrimEnd('/', '#', '?').ToLowerInvariant();

    public bool IsValidUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    [GeneratedRegex(
        @"https?://(?:www\.)?github\.com/([^/]+)/([^/]+)(?:/.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        "en-SG"
    )]
    private static partial Regex GitHubUrlRegex();

    public static (string? Owner, string? RepoName, long? IssueNumber) ParseGitHubIssueUrl(
        string issueUrl,
        ILogger logger
    )
    {
        var isUri = Uri.TryCreate(issueUrl, UriKind.Absolute, out var uri);
        if (!isUri || uri == null || uri.Segments.Length < 5)
        {
            logger.LogWarning("Invalid GitHub issue URL format: {Url}", issueUrl);
            return (null, null, null);
        }

        var owner = uri.Segments[1].TrimEnd('/');
        var repoName = uri.Segments[2].TrimEnd('/');
        var shouldBeIssueNumber = uri.Segments[4].TrimEnd('/');
        if (!long.TryParse(shouldBeIssueNumber, out var parsedIssueNumber))
        {
            logger.LogWarning("Could not parse issue number from URL: {Url}", issueUrl);
            return (null, null, null);
        }
        return (owner, repoName, parsedIssueNumber);
    }
}
