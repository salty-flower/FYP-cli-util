using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using DataCollection.Core.Models.IssueTracker;
using DataCollection.Infrastructure.Models;
using DataCollection.Infrastructure.Models.Bugzilla;
using Microsoft.Extensions.Logging;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

public class BugzillaClient : BaseIssueTrackerClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BugzillaClient> _logger;
    private readonly ConcurrentDictionary<string, UniversalUserProfile> _userProfileCache = new();
    private readonly ConcurrentDictionary<string, Repository> _repositoryCache = new();

    // Role detection patterns based on analysis
    private static readonly Regex MaintainerPattern = new(@"\[:([^\]]+)\]", RegexOptions.Compiled);
    private static readonly Regex SpecialRolePattern = new(@"[✱★☆]", RegexOptions.Compiled);
    private static readonly Regex GlobPattern = new(
        @"\bglob\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    public BugzillaClient(HttpClient httpClient, ILogger<BugzillaClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public override BugTrackingProvider Provider => BugTrackingProvider.Bugzilla;

    public override async Task<bool> IsValidRepositoryUrlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        // Extract hostname from URL
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        try
        {
            var response = await _httpClient.GetAsync($"{uri.Scheme}://{uri.Host}/rest/version");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate Bugzilla instance at {Url}", url);
            return false;
        }
    }

    public override async Task<Repository?> GetRepositoryAsync(string identifier)
    {
        if (_repositoryCache.TryGetValue(identifier, out var cachedRepo))
            return cachedRepo;

        try
        {
            // For Bugzilla, identifier is typically "hostname/product"
            var parts = identifier.Split('/');
            if (parts.Length < 2)
                throw new ArgumentException(
                    $"Invalid Bugzilla repository identifier: {identifier}"
                );

            var hostname = parts[0];
            var productName = parts[1];
            var baseUrl = $"https://{hostname}";

            var response = await _httpClient.GetStringAsync(
                $"{baseUrl}/rest/product?names={productName}"
            );
            var productsResponse = JsonSerializer.Deserialize<BugzillaProductsResponse>(response);
            var product = productsResponse?.Products?.FirstOrDefault();

            if (product == null)
                return null;

            var repository = new Repository
            {
                Identifier = identifier,
                Name = product.Name,
                Description = product.Description,
                Url =
                    $"{baseUrl}/describecomponents.cgi?product={Uri.EscapeDataString(product.Name)}",
                Provider = BugTrackingProvider.Bugzilla,
                ProviderSpecificData = new BugzillaRepositoryData
                {
                    Product = product.Name,
                    Description = product.Description,
                    Components = [], // Would need additional API call to get components
                    Versions = [], // Would need additional API call to get versions
                    Milestones = [], // Would need additional API call to get milestones
                },
            };

            _repositoryCache[identifier] = repository;
            return repository;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get repository {Identifier} from Bugzilla", identifier);
            return null;
        }
    }

    public override async Task<Issue?> GetIssueAsync(string repositoryIdentifier, string issueId)
    {
        try
        {
            var parts = repositoryIdentifier.Split('/');
            if (parts.Length < 2)
                throw new ArgumentException(
                    $"Invalid repository identifier: {repositoryIdentifier}"
                );

            var baseUrl = $"https://{parts[0]}";
            var response = await _httpClient.GetStringAsync(
                $"{baseUrl}/rest/bug/{issueId}?include_fields=*"
            );
            var bugResponse = JsonSerializer.Deserialize<BugzillaBugResponse>(response);
            var bug = bugResponse?.Bugs?.FirstOrDefault();

            if (bug == null)
                return null;

            return new Issue
            {
                Id = bug.Id.ToString(),
                Title = bug.Summary,
                Status = MapToUniversalStatus(bug.Status, BugTrackingProvider.Bugzilla),
                Author = bug.CreatorDetail?.RealName ?? bug.Creator,
                CreatedAt = bug.CreationTime,
                UpdatedAt = bug.LastChangeTime,
                ClosedAt = IsClosedStatus(bug.Status) ? bug.LastChangeTime : null,
                Labels = bug.Keywords,
                Assignees = string.IsNullOrEmpty(bug.AssignedTo)
                    ? []
                    : [bug.AssignedToDetail?.RealName ?? bug.AssignedTo],
                Priority = bug.Priority,
                Severity = bug.Severity,
                Provider = BugTrackingProvider.Bugzilla,
                ProviderSpecificData = new BugzillaIssueData
                {
                    BugId = (int)bug.Id,
                    Product = bug.Product,
                    Component = bug.Component,
                    Version = bug.Version,
                    TargetMilestone = "---", // Default value from Bugzilla
                    OperatingSystem = bug.OperatingSystem,
                    Platform = bug.Platform,
                    Severity = bug.Severity,
                    Classification = bug.Classification,
                },
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get issue {IssueId} from repository {RepositoryIdentifier}",
                issueId,
                repositoryIdentifier
            );
            return null;
        }
    }

    public override async Task<List<Issue>> SearchIssuesAsync(
        string repositoryIdentifier,
        IssueSearchQuery query
    )
    {
        try
        {
            var parts = repositoryIdentifier.Split('/');
            if (parts.Length < 2)
                throw new ArgumentException(
                    $"Invalid repository identifier: {repositoryIdentifier}"
                );

            var baseUrl = $"https://{parts[0]}";
            var productName = parts[1];

            var searchParams = new List<string> { $"product={Uri.EscapeDataString(productName)}" };

            if (!string.IsNullOrEmpty(query.Query))
                searchParams.Add($"quicksearch={Uri.EscapeDataString(query.Query)}");

            if (query.Status.HasValue)
                searchParams.Add($"status={GetBugzillaStatus(query.Status.Value)}");

            if (!string.IsNullOrEmpty(query.Author))
                searchParams.Add($"creator={Uri.EscapeDataString(query.Author)}");

            if (!string.IsNullOrEmpty(query.Assignee))
                searchParams.Add($"assigned_to={Uri.EscapeDataString(query.Assignee)}");

            if (query.CreatedAfter.HasValue)
                searchParams.Add($"created_after={query.CreatedAfter.Value:yyyy-MM-dd}");

            if (query.CreatedBefore.HasValue)
                searchParams.Add($"created_before={query.CreatedBefore.Value:yyyy-MM-dd}");

            if (query.Limit.HasValue)
                searchParams.Add($"limit={query.Limit.Value}");

            if (query.Offset.HasValue)
                searchParams.Add($"offset={query.Offset.Value}");

            var searchUrl = $"{baseUrl}/rest/bug?{string.Join("&", searchParams)}&include_fields=*";
            var response = await _httpClient.GetStringAsync(searchUrl);
            var searchResponse = JsonSerializer.Deserialize<BugzillaSearchResponse>(response);

            return searchResponse
                    ?.Bugs?.Select(bug => new Issue
                    {
                        Id = bug.Id.ToString(),
                        Title = bug.Summary,
                        Status = MapToUniversalStatus(bug.Status, BugTrackingProvider.Bugzilla),
                        Author = bug.CreatorDetail?.RealName ?? bug.Creator,
                        CreatedAt = bug.CreationTime,
                        UpdatedAt = bug.LastChangeTime,
                        ClosedAt = IsClosedStatus(bug.Status) ? bug.LastChangeTime : null,
                        Labels = bug.Keywords,
                        Assignees = string.IsNullOrEmpty(bug.AssignedTo)
                            ? []
                            : [bug.AssignedToDetail?.RealName ?? bug.AssignedTo],
                        Priority = bug.Priority,
                        Severity = bug.Severity,
                        Provider = BugTrackingProvider.Bugzilla,
                        ProviderSpecificData = new BugzillaIssueData
                        {
                            BugId = (int)bug.Id,
                            Product = bug.Product,
                            Component = bug.Component,
                            Version = bug.Version,
                            TargetMilestone = "---", // Default value from Bugzilla
                            OperatingSystem = bug.OperatingSystem,
                            Platform = bug.Platform,
                            Severity = bug.Severity,
                            Classification = bug.Classification,
                        },
                    })
                    .ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to search issues in repository {RepositoryIdentifier}",
                repositoryIdentifier
            );
            return [];
        }
    }

    public override async Task<List<Comment>> GetIssueCommentsAsync(
        string repositoryIdentifier,
        string issueId
    )
    {
        try
        {
            var parts = repositoryIdentifier.Split('/');
            if (parts.Length < 2)
                throw new ArgumentException(
                    $"Invalid repository identifier: {repositoryIdentifier}"
                );

            var baseUrl = $"https://{parts[0]}";
            var response = await _httpClient.GetStringAsync(
                $"{baseUrl}/rest/bug/{issueId}/comment"
            );
            var commentsResponse = JsonSerializer.Deserialize<BugzillaCommentsResponse>(response);
            var bugComments = commentsResponse?.Bugs?.Values?.FirstOrDefault();

            if (bugComments == null)
                return [];

            return bugComments
                .Comments.Select(comment => new Comment
                {
                    Id = comment.Id.ToString(),
                    Author = comment.Creator,
                    Content = comment.Text,
                    CreatedAt = comment.Time,
                    UpdatedAt = comment.CreationTime,
                    Provider = BugTrackingProvider.Bugzilla,
                    ProviderSpecificData = null, // Comments don't have specific Bugzilla data class defined
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get comments for issue {IssueId} from repository {RepositoryIdentifier}",
                issueId,
                repositoryIdentifier
            );
            return [];
        }
    }

    public override async Task<List<IssueEvent>> GetIssueEventsAsync(
        string repositoryIdentifier,
        string issueId
    )
    {
        try
        {
            var parts = repositoryIdentifier.Split('/');
            if (parts.Length < 2)
                throw new ArgumentException(
                    $"Invalid repository identifier: {repositoryIdentifier}"
                );

            var baseUrl = $"https://{parts[0]}";
            var response = await _httpClient.GetStringAsync(
                $"{baseUrl}/rest/bug/{issueId}/history"
            );
            var historyResponse = JsonSerializer.Deserialize<BugzillaHistoryResponse>(response);
            var bugHistory = historyResponse?.Bugs?.FirstOrDefault();

            if (bugHistory == null)
                return [];

            var events = new List<IssueEvent>();

            foreach (var historyEvent in bugHistory.History)
            {
                foreach (var change in historyEvent.Changes)
                {
                    events.Add(
                        new IssueEvent
                        {
                            Id = $"{historyEvent.When.Ticks}_{change.FieldName}",
                            EventType = MapFieldNameToEventType(change.FieldName),
                            Actor = historyEvent.Who,
                            OccurredAt = historyEvent.When,
                            Description =
                                $"Changed {change.FieldName} from '{change.Removed}' to '{change.Added}'",
                            Provider = BugTrackingProvider.Bugzilla,
                            ProviderSpecificData = null, // Events don't have specific Bugzilla data class defined
                        }
                    );
                }
            }

            return events;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get events for issue {IssueId} from repository {RepositoryIdentifier}",
                issueId,
                repositoryIdentifier
            );
            return [];
        }
    }

    public override async Task<UniversalUserProfile?> GetUserProfileAsync(string username)
    {
        if (_userProfileCache.TryGetValue(username, out var cachedProfile))
            return cachedProfile;

        try
        {
            // For Bugzilla, we need to search across multiple instances or use a specific one
            // This is a simplified implementation - in practice, you'd need the Bugzilla hostname
            var profile = new UniversalUserProfile
            {
                Username = username,
                Provider = BugTrackingProvider.Bugzilla,
                TotalIssuesOpened = 0, // Would need to search across repositories
                TotalIssuesAssigned = 0, // Would need to search across repositories
                TotalCommentsPosted = 0, // Would need to search across repositories
                ActivityScore = 0.0,
            };

            // Extract role information from username/real_name if available
            ExtractRoleInformation(profile, username);

            _userProfileCache[username] = profile;
            return profile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user profile for {Username}", username);
            return null;
        }
    }

    public override async Task<List<string>> GetRepositoryContributorsAsync(
        string repositoryIdentifier
    )
    {
        try
        {
            // This would typically require searching all bugs for unique users
            // For now, return empty list as this requires more complex queries
            _logger.LogWarning("GetRepositoryContributorsAsync not fully implemented for Bugzilla");
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get contributors for repository {RepositoryIdentifier}",
                repositoryIdentifier
            );
            return [];
        }
    }

    public override async Task<List<Repository>> SearchRepositoriesAsync(string query)
    {
        try
        {
            // Bugzilla doesn't have a direct repository search - you search for products
            // This is a simplified implementation
            _logger.LogWarning("SearchRepositoriesAsync not fully implemented for Bugzilla");
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search repositories with query {Query}", query);
            return [];
        }
    }

    private void ExtractRoleInformation(UniversalUserProfile profile, string realName)
    {
        // Extract role indicators based on analysis findings
        var roleIndicators = new List<string>();

        // Check for maintainer pattern like [:aryx], [:username]
        var maintainerMatch = MaintainerPattern.Match(realName);
        if (maintainerMatch.Success)
        {
            profile.IsMaintainer = true;
            roleIndicators.Add($"Maintainer: {maintainerMatch.Groups[1].Value}");
        }

        // Check for special role symbols like ✱, ★, ☆
        if (SpecialRolePattern.IsMatch(realName))
        {
            profile.IsTriageOwner = true;
            roleIndicators.Add("Special role symbols detected");
        }

        // Check for "glob" indicator
        if (GlobPattern.IsMatch(realName))
        {
            profile.IsCommitter = true;
            roleIndicators.Add("Glob role detected");
        }

        // Store raw role indicators for later analysis
        if (roleIndicators.Count > 0)
        {
            profile.RoleIndicators = string.Join("; ", roleIndicators);
        }

        // Set general developer flag if any role is detected
        if (
            profile.IsMaintainer == true
            || profile.IsCommitter == true
            || profile.IsTriageOwner == true
        )
        {
            profile.IsDeveloper = true;
        }
    }

    private static string GetBugzillaStatus(IssueStatus status) =>
        status switch
        {
            IssueStatus.Open => "NEW,UNCONFIRMED,ASSIGNED",
            IssueStatus.InProgress => "ASSIGNED",
            IssueStatus.Resolved => "RESOLVED",
            IssueStatus.Closed => "VERIFIED,CLOSED",
            IssueStatus.Duplicate => "RESOLVED",
            IssueStatus.Invalid => "RESOLVED",
            IssueStatus.Wontfix => "RESOLVED",
            _ => "NEW,UNCONFIRMED,ASSIGNED,RESOLVED,VERIFIED,CLOSED",
        };

    private static bool IsClosedStatus(string status) =>
        status.ToUpperInvariant() switch
        {
            "RESOLVED" or "VERIFIED" or "CLOSED" => true,
            _ => false,
        };

    private static string MapFieldNameToEventType(string fieldName) =>
        fieldName.ToLowerInvariant() switch
        {
            "assigned_to" => "assigned",
            "status" => "status_changed",
            "resolution" => "resolved",
            "priority" => "priority_changed",
            "severity" => "severity_changed",
            "cc" => "cc_changed",
            "keywords" => "labeled",
            "component" => "component_changed",
            "version" => "version_changed",
            "target_milestone" => "milestone_changed",
            _ => fieldName,
        };
}
