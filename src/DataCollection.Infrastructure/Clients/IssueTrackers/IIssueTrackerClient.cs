using DataCollection.Core.Models.IssueTracker;
using DataCollection.Infrastructure.Models;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

public interface IIssueTrackerClient
{
    BugTrackingProvider Provider { get; }
    Task<bool> IsValidRepositoryUrlAsync(string url);
    Task<Repository?> GetRepositoryAsync(string identifier);
    Task<Issue?> GetIssueAsync(string repositoryIdentifier, string issueId);
    Task<List<Issue>> SearchIssuesAsync(string repositoryIdentifier, IssueSearchQuery query);
    Task<List<Comment>> GetIssueCommentsAsync(string repositoryIdentifier, string issueId);
    Task<List<IssueEvent>> GetIssueEventsAsync(string repositoryIdentifier, string issueId);
    Task<UniversalUserProfile?> GetUserProfileAsync(string username);
    Task<List<string>> GetRepositoryContributorsAsync(string repositoryIdentifier);
    Task<List<Repository>> SearchRepositoriesAsync(string query);
}

public interface IIssueTrackerClientFactory
{
    IIssueTrackerClient CreateClient(BugTrackingProvider provider);
    BugTrackingProvider? DetectProviderFromUrl(string url);
    string? ExtractRepositoryIdentifier(string url, BugTrackingProvider provider);
    string? ExtractIssueId(string url, BugTrackingProvider provider);
}

public abstract class BaseIssueTrackerClient : IIssueTrackerClient
{
    public abstract BugTrackingProvider Provider { get; }

    public abstract Task<bool> IsValidRepositoryUrlAsync(string url);
    public abstract Task<Repository?> GetRepositoryAsync(string identifier);
    public abstract Task<Issue?> GetIssueAsync(string repositoryIdentifier, string issueId);
    public abstract Task<List<Issue>> SearchIssuesAsync(
        string repositoryIdentifier,
        IssueSearchQuery query
    );
    public abstract Task<List<Comment>> GetIssueCommentsAsync(
        string repositoryIdentifier,
        string issueId
    );
    public abstract Task<List<IssueEvent>> GetIssueEventsAsync(
        string repositoryIdentifier,
        string issueId
    );
    public abstract Task<UniversalUserProfile?> GetUserProfileAsync(string username);
    public abstract Task<List<string>> GetRepositoryContributorsAsync(string repositoryIdentifier);
    public abstract Task<List<Repository>> SearchRepositoriesAsync(string query);

    public static IssueStatus MapToUniversalStatus(
        string providerStatus,
        BugTrackingProvider provider
    ) =>
        provider switch
        {
            BugTrackingProvider.GitHub => MapGitHubStatus(providerStatus),
            BugTrackingProvider.Jira => MapJiraStatus(providerStatus),
            BugTrackingProvider.Bugzilla => MapBugzillaStatus(providerStatus),
            BugTrackingProvider.GitLab => MapGitLabStatus(providerStatus),
            BugTrackingProvider.GnuSavannah => MapGnuSavannahStatus(providerStatus),
            BugTrackingProvider.Trac => MapTracStatus(providerStatus),
            _ => IssueStatus.Unknown,
        };

    private static IssueStatus MapGitHubStatus(string status) =>
        status.ToLowerInvariant() switch
        {
            "open" => IssueStatus.Open,
            "closed" => IssueStatus.Closed,
            _ => IssueStatus.Unknown,
        };

    private static IssueStatus MapJiraStatus(string status) =>
        status.ToLowerInvariant() switch
        {
            "open" or "to do" or "new" => IssueStatus.Open,
            "in progress" or "in review" => IssueStatus.InProgress,
            "done" or "resolved" => IssueStatus.Resolved,
            "closed" => IssueStatus.Closed,
            "won't do" or "won't fix" => IssueStatus.Wontfix,
            "duplicate" => IssueStatus.Duplicate,
            "invalid" => IssueStatus.Invalid,
            _ => IssueStatus.Unknown,
        };

    private static IssueStatus MapBugzillaStatus(string? status) =>
        status?.ToLowerInvariant() switch
        {
            "new" or "unconfirmed" or "assigned" => IssueStatus.Open,
            "resolved" => IssueStatus.Resolved,
            "verified" or "closed" => IssueStatus.Closed,
            "duplicate" => IssueStatus.Duplicate,
            "invalid" => IssueStatus.Invalid,
            "wontfix" => IssueStatus.Wontfix,
            null => IssueStatus.Unknown,
            _ => IssueStatus.Unknown,
        };

    private static IssueStatus MapGitLabStatus(string status) =>
        status.ToLowerInvariant() switch
        {
            "opened" => IssueStatus.Open,
            "closed" => IssueStatus.Closed,
            _ => IssueStatus.Unknown,
        };

    private static IssueStatus MapGnuSavannahStatus(string status) =>
        status.ToLowerInvariant() switch
        {
            "open" or "none" => IssueStatus.Open,
            "closed" => IssueStatus.Closed,
            "fixed" => IssueStatus.Resolved,
            "invalid" => IssueStatus.Invalid,
            "duplicate" => IssueStatus.Duplicate,
            "wontfix" or "works for me" => IssueStatus.Wontfix,
            _ => IssueStatus.Unknown,
        };

    private static IssueStatus MapTracStatus(string status) =>
        status.ToLowerInvariant() switch
        {
            "new" or "assigned" or "accepted" => IssueStatus.Open,
            "closed" => IssueStatus.Closed,
            "fixed" => IssueStatus.Resolved,
            "invalid" => IssueStatus.Invalid,
            "duplicate" => IssueStatus.Duplicate,
            "wontfix" => IssueStatus.Wontfix,
            _ => IssueStatus.Unknown,
        };
}
