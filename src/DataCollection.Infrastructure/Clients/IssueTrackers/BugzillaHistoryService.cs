using System.Collections.Concurrent;
using DataCollection.Core.Models.IssueTracker;
using Microsoft.Extensions.Logging;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

/// <summary>
/// Service for retrieving Bugzilla issue history using Refit with proper rate limiting and retry policies
/// Handles dynamic base URL configuration for different Bugzilla instances
/// </summary>
public class BugzillaHistoryService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BugzillaHistoryService> _logger;
    private readonly ConcurrentDictionary<string, IBugzillaApi> _bugzillaApiCache = new();

    public BugzillaHistoryService(
        IHttpClientFactory httpClientFactory,
        ILogger<BugzillaHistoryService> logger
    )
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Get issue history/changelog for any Bugzilla instance
    /// </summary>
    public async Task<List<IssueEvent>> GetIssueHistoryAsync(string hostname, int bugId)
    {
        try
        {
            var bugzillaApi = GetOrCreateBugzillaApi(hostname);
            var historyResponse = await bugzillaApi.GetHistoryAsync(bugId);

            var events = new List<IssueEvent>();

            foreach (var historyBug in historyResponse.Bugs)
            {
                foreach (var historyEvent in historyBug.History)
                {
                    foreach (var change in historyEvent.Changes)
                    {
                        events.Add(
                            new IssueEvent
                            {
                                Id =
                                    $"{historyBug.Id}_{historyEvent.When.Ticks}_{change.FieldName}",
                                EventType = MapFieldNameToEventType(change.FieldName),
                                Actor = historyEvent.Who,
                                OccurredAt = historyEvent.When,
                                Description = BuildChangeDescription(change),
                                Provider = BugTrackingProvider.Bugzilla,
                            }
                        );
                    }
                }
            }

            _logger.LogDebug(
                "Retrieved {Count} history events for bug {BugId} from {Hostname}",
                events.Count,
                bugId,
                hostname
            );
            return events;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get Bugzilla history for bug {BugId} from {Hostname}",
                bugId,
                hostname
            );
            return [];
        }
    }

    /// <summary>
    /// Get issue details using REST API for any Bugzilla instance
    /// </summary>
    public async Task<Issue?> GetIssueDetailsAsync(string hostname, int bugId)
    {
        try
        {
            var bugzillaApi = GetOrCreateBugzillaApi(hostname);
            var bugResponse = await bugzillaApi.GetBugAsync(bugId);
            var bug = bugResponse?.Bugs?.FirstOrDefault();

            if (bug == null)
                return null;

            return new Issue
            {
                Id = bug.Id.ToString(),
                Title = bug.Summary,
                Status = BaseIssueTrackerClient.MapToUniversalStatus(
                    bug.Status ?? "",
                    BugTrackingProvider.Bugzilla
                ),
                Author = bug.CreatorDetail?.RealName ?? bug.Creator,
                CreatedAt = bug.CreationTime,
                UpdatedAt = bug.LastChangeTime,
                ClosedAt = IsClosedStatus(bug.Status) ? bug.LastChangeTime : null,
                Labels = bug.Keywords?.ToList() ?? [],
                Assignees = string.IsNullOrEmpty(bug.AssignedTo)
                    ? []
                    : [bug.AssignedToDetail?.RealName ?? bug.AssignedTo],
                Priority = bug.Priority,
                Severity = bug.Severity,
                Provider = BugTrackingProvider.Bugzilla,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get Bugzilla issue details for bug {BugId} from {Hostname}",
                bugId,
                hostname
            );
            return null;
        }
    }

    /// <summary>
    /// Get enhanced comments for any Bugzilla instance
    /// </summary>
    public async Task<List<Comment>> GetEnhancedCommentsAsync(string hostname, int bugId)
    {
        try
        {
            var bugzillaApi = GetOrCreateBugzillaApi(hostname);
            var commentsResponse = await bugzillaApi.GetCommentsAsync(bugId);

            var comments = new List<Comment>();

            // Bugzilla returns comments grouped by bug ID
            if (commentsResponse.Bugs.TryGetValue(bugId.ToString(), out var bugComments))
            {
                foreach (var comment in bugComments.Comments)
                {
                    comments.Add(
                        new Comment
                        {
                            Id = comment.Id.ToString(),
                            Author = comment.Creator,
                            Content = comment.Text,
                            CreatedAt = comment.Time,
                            UpdatedAt = comment.CreationTime,
                            Provider = BugTrackingProvider.Bugzilla,
                        }
                    );
                }
            }

            _logger.LogDebug(
                "Retrieved {Count} comments for bug {BugId} from {Hostname}",
                comments.Count,
                bugId,
                hostname
            );

            return comments.OrderBy(c => c.CreatedAt).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get enhanced comments for bug {BugId} from {Hostname}",
                bugId,
                hostname
            );
            return [];
        }
    }

    private IBugzillaApi GetOrCreateBugzillaApi(string hostname)
    {
        return _bugzillaApiCache.GetOrAdd(hostname, CreateBugzillaApiForHost);
    }

    private IBugzillaApi CreateBugzillaApiForHost(string hostname)
    {
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.BaseAddress = new Uri($"https://{hostname}");

        // Create Refit client with the configured HttpClient that already has
        // rate limiting and retry policies from ServiceRegistration
        var refitSettings = new Refit.RefitSettings();
        return Refit.RestService.For<IBugzillaApi>(httpClient, refitSettings);
    }

    private static string BuildChangeDescription(BugzillaHistoryChange change)
    {
        return change.FieldName.ToLowerInvariant() switch
        {
            "status" => $"Status changed from '{change.Removed}' to '{change.Added}'",
            "assigned_to" =>
                $"Assignee changed from '{change.Removed ?? "Unassigned"}' to '{change.Added ?? "Unassigned"}'",
            "priority" => $"Priority changed from '{change.Removed}' to '{change.Added}'",
            "keywords" => $"Keywords changed from '{change.Removed}' to '{change.Added}'",
            "resolution" => $"Resolution changed from '{change.Removed}' to '{change.Added}'",
            "target_milestone" =>
                $"Target milestone changed from '{change.Removed}' to '{change.Added}'",
            "component" => $"Component changed from '{change.Removed}' to '{change.Added}'",
            "product" => $"Product changed from '{change.Removed}' to '{change.Added}'",
            "version" => $"Version changed from '{change.Removed}' to '{change.Added}'",
            "severity" => $"Severity changed from '{change.Removed}' to '{change.Added}'",
            "cc" => $"CC list updated: {change.Added}",
            _ => $"{change.FieldName} changed from '{change.Removed}' to '{change.Added}'",
        };
    }

    private static string MapFieldNameToEventType(string fieldName)
    {
        return fieldName.ToLowerInvariant() switch
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
            "product" => "product_changed",
            _ => $"{fieldName}_changed",
        };
    }

    private static bool IsClosedStatus(string? status) =>
        status?.ToUpperInvariant() switch
        {
            "RESOLVED" or "VERIFIED" or "CLOSED" => true,
            _ => false,
        };
}
