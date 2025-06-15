using System.Text.RegularExpressions;
using DataCollection.Core.Models.IssueTracker;
using Microsoft.Extensions.DependencyInjection;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

public class IssueTrackerClientFactory(IServiceProvider serviceProvider)
    : IIssueTrackerClientFactory
{
    private static readonly Dictionary<BugTrackingProvider, Regex[]> ProviderUrlPatterns = new()
    {
        [BugTrackingProvider.GitHub] =
        [
            new Regex(
                @"github\.com/([^/]+)/([^/]+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            ),
            new Regex(
                @"api\.github\.com/repos/([^/]+)/([^/]+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            ),
        ],
        [BugTrackingProvider.GitLab] =
        [
            new Regex(
                @"gitlab\.com/([^/]+)/([^/]+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            ),
            new Regex(
                @"gitlab\.([^/]+)/([^/]+)/([^/]+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            ),
        ],
        [BugTrackingProvider.Jira] =
        [
            new Regex(
                @"([^/]+)\.atlassian\.net/browse/([A-Z]+-\d+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            ),
            new Regex(
                @"jira\.([^/]+)/browse/([A-Z]+-\d+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            ),
        ],
        [BugTrackingProvider.Bugzilla] =
        [
            new Regex(
                @"bugzilla\.([^/]+)/show_bug\.cgi\?id=(\d+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            ),
            new Regex(
                @"([^/]+)/bugzilla/show_bug\.cgi\?id=(\d+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            ),
        ],
    };

    private static readonly Dictionary<BugTrackingProvider, Regex[]> RepositoryPatterns = new()
    {
        [BugTrackingProvider.GitHub] =
        [
            new Regex(
                @"github\.com/([^/]+)/([^/]+?)(?:/|$)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            ),
        ],
        [BugTrackingProvider.GitLab] =
        [
            new Regex(
                @"gitlab\.com/([^/]+)/([^/]+?)(?:/|$)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            ),
            new Regex(
                @"gitlab\.([^/]+)/([^/]+)/([^/]+?)(?:/|$)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            ),
        ],
        [BugTrackingProvider.Jira] =
        [
            new Regex(
                @"([^/]+)\.atlassian\.net/projects/([A-Z]+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            ),
        ],
        [BugTrackingProvider.Bugzilla] =
        [
            new Regex(
                @"bugzilla\.([^/]+)/describecomponents\.cgi\?product=([^&]+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            ),
        ],
    };

    private static readonly Dictionary<BugTrackingProvider, Regex[]> IssueIdPatterns = new()
    {
        [BugTrackingProvider.GitHub] =
        [
            new Regex(@"/issues/(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"/pull/(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"#(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ],
        [BugTrackingProvider.GitLab] =
        [
            new Regex(@"/-/issues/(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"/-/merge_requests/(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ],
        [BugTrackingProvider.Jira] =
        [
            new Regex(@"/browse/([A-Z]+-\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"([A-Z]+-\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ],
        [BugTrackingProvider.Bugzilla] =
        [
            new Regex(@"show_bug\.cgi\?id=(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"bug\.cgi\?id=(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ],
    };

    public IIssueTrackerClient CreateClient(BugTrackingProvider provider)
    {
        return provider switch
        {
            BugTrackingProvider.GitHub => throw new NotImplementedException(
                "GitHub client will be implemented later"
            ),
            BugTrackingProvider.Jira => throw new NotImplementedException(
                "Jira client not yet implemented"
            ),
            BugTrackingProvider.GitLab => throw new NotImplementedException(
                "GitLab client not yet implemented"
            ),
            BugTrackingProvider.Bugzilla => throw new NotImplementedException(
                "Bugzilla client not yet implemented"
            ),
            _ => throw new ArgumentException($"Unsupported provider: {provider}"),
        };
    }

    public BugTrackingProvider? DetectProviderFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        foreach (var (provider, patterns) in ProviderUrlPatterns)
        {
            if (patterns.Any(pattern => pattern.IsMatch(url)))
                return provider;
        }

        return BugTrackingProvider.Unknown;
    }

    public string? ExtractRepositoryIdentifier(string url, BugTrackingProvider provider)
    {
        if (
            string.IsNullOrWhiteSpace(url)
            || !RepositoryPatterns.TryGetValue(provider, out var patterns)
        )
            return null;

        foreach (var pattern in patterns)
        {
            var match = pattern.Match(url);
            if (match.Success)
            {
                return provider switch
                {
                    BugTrackingProvider.GitHub =>
                        $"{match.Groups[1].Value}/{match.Groups[2].Value}",
                    BugTrackingProvider.GitLab when match.Groups.Count >= 4 =>
                        $"{match.Groups[2].Value}/{match.Groups[3].Value}",
                    BugTrackingProvider.GitLab =>
                        $"{match.Groups[1].Value}/{match.Groups[2].Value}",
                    BugTrackingProvider.Jira => match.Groups[2].Value, // Project key
                    BugTrackingProvider.Bugzilla => match.Groups[2].Value, // Product name
                    _ => null,
                };
            }
        }

        return null;
    }

    public string? ExtractIssueId(string url, BugTrackingProvider provider)
    {
        if (
            string.IsNullOrWhiteSpace(url)
            || !IssueIdPatterns.TryGetValue(provider, out var patterns)
        )
            return null;

        foreach (var pattern in patterns)
        {
            var match = pattern.Match(url);
            if (match.Success)
            {
                return provider switch
                {
                    BugTrackingProvider.GitHub => match.Groups[1].Value,
                    BugTrackingProvider.GitLab => match.Groups[1].Value,
                    BugTrackingProvider.Jira => match.Groups[1].Value,
                    BugTrackingProvider.Bugzilla => match.Groups[1].Value,
                    _ => null,
                };
            }
        }

        return null;
    }
}
