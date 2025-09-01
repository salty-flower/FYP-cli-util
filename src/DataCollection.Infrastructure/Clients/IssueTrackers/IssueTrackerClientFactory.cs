using System.Text.RegularExpressions;
using DataCollection.Core.Models.IssueTracker;
using Microsoft.Extensions.Logging;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

public class IssueTrackerClientFactory : IIssueTrackerClientFactory
{
    private readonly HttpClient _httpClient;
    private readonly ILoggerFactory _loggerFactory;
    private readonly JiraHistoryService? _jiraHistoryService;
    private readonly BugzillaHistoryService? _bugzillaHistoryService;

    public IssueTrackerClientFactory(
        HttpClient httpClient,
        ILoggerFactory loggerFactory,
        JiraHistoryService? jiraHistoryService = null,
        BugzillaHistoryService? bugzillaHistoryService = null
    )
    {
        _httpClient = httpClient;
        _loggerFactory = loggerFactory;
        _jiraHistoryService = jiraHistoryService;
        _bugzillaHistoryService = bugzillaHistoryService;
    }

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
            new Regex(
                @"(bugs\.[^/]+)/browse/([A-Z]+-\d+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            ),
            new Regex(
                @"([^/]+\.[^/]+)/jira/browse/([A-Z]+-\d+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            ),
        ],
        [BugTrackingProvider.Bugzilla] =
        [
            new Regex(
                @"(bugzilla\.[^/]+)/show_bug\.cgi\?id=(\d+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            ),
            new Regex(
                @"(bugs\.[^/]+)/show_bug\.cgi\?id=(\d+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            ),
            new Regex(
                @"([^/]+)/bugzilla/show_bug\.cgi\?id=(\d+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            ),
        ],
        [BugTrackingProvider.GnuSavannah] =
        [
            new Regex(
                @"savannah\.gnu\.org/bugs/\?(\d+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            ),
            new Regex(
                @"savannah\.([^/]+)/bugs/\?(\d+)",
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
            new Regex(
                @"(bugs\.[^/]+)/browse/([A-Z]+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            ),
        ],
        [BugTrackingProvider.Bugzilla] =
        [
            new Regex(
                @"bugzilla\.([^/]+)/describecomponents\.cgi\?product=([^&]+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            ),
            new Regex(
                @"(bugs\.[^/]+)/show_bug\.cgi\?id=(\d+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            ),
        ],
        [BugTrackingProvider.GnuSavannah] =
        [
            new Regex(
                @"savannah\.gnu\.org/(projects/)?([^/]+)",
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
        [BugTrackingProvider.GnuSavannah] =
        [
            new Regex(@"/bugs/\?(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ],
    };

    public IIssueTrackerClient CreateClient(BugTrackingProvider provider)
    {
        return provider switch
        {
            BugTrackingProvider.GitHub => throw new NotImplementedException(
                "GitHub client will be implemented later"
            ),
            BugTrackingProvider.Jira => new JiraClient(
                _httpClient,
                _loggerFactory.CreateLogger<JiraClient>(),
                _jiraHistoryService
            ),
            BugTrackingProvider.GitLab => throw new NotImplementedException(
                "GitLab client not yet implemented"
            ),
            BugTrackingProvider.Bugzilla => new BugzillaClient(
                _httpClient,
                _loggerFactory.CreateLogger<BugzillaClient>(),
                _bugzillaHistoryService
            ),
            BugTrackingProvider.GnuSavannah => throw new NotImplementedException(
                "GNU Savannah client not yet implemented"
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
                    BugTrackingProvider.Jira => match.Groups.Count >= 3
                        ? $"{match.Groups[1].Value}/{match.Groups[2].Value}" // hostname/project for bugs.openjdk.org and atlassian.net patterns
                        : match.Groups[2].Value, // Just project key fallback
                    BugTrackingProvider.Bugzilla => pattern.ToString().Contains("bugs\\.")
                        ? $"{match.Groups[1].Value}/WebKit" // For bugs.webkit.org format - assume WebKit product
                        : match.Groups[2].Value, // Product name for other formats
                    BugTrackingProvider.GnuSavannah => match.Groups[2].Value, // Project name
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
                    BugTrackingProvider.GnuSavannah => match.Groups[1].Value,
                    _ => null,
                };
            }
        }

        return null;
    }
}
