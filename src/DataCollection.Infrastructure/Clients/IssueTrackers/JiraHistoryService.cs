using System.Collections.Concurrent;
using DataCollection.Core.Models.IssueTracker;
using Microsoft.Extensions.Logging;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

/// <summary>
/// Service for retrieving Jira issue history using Refit with proper rate limiting and retry policies
/// Handles dynamic base URL configuration for different Jira instances
/// </summary>
public class JiraHistoryService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<JiraHistoryService> _logger;
    private readonly ConcurrentDictionary<string, IJiraApi> _jiraApiCache = new();

    public JiraHistoryService(
        IHttpClientFactory httpClientFactory,
        ILogger<JiraHistoryService> logger
    )
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Get issue history/changelog for any Jira instance
    /// Uses separate changelog endpoint for Jira Cloud, falls back to expand=changelog for Jira Server
    /// </summary>
    public async Task<List<IssueEvent>> GetIssueHistoryAsync(string hostname, string issueKey)
    {
        try
        {
            var jiraApi = GetOrCreateJiraApi(hostname);

            // First, try the dedicated changelog endpoint (Jira Cloud)
            try
            {
                var changelog = await jiraApi.GetChangelogAsync(issueKey);
                var events = ExtractEventsFromChangelog(changelog);

                _logger.LogDebug(
                    "Retrieved {Count} history events via changelog endpoint for {IssueKey} from {Hostname}",
                    events.Count,
                    issueKey,
                    hostname
                );
                return events;
            }
            catch (Refit.ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogDebug(
                    "Changelog endpoint not available (404), falling back to expand=changelog for {IssueKey}",
                    issueKey
                );

                // Fallback: Use main issue endpoint with expand=changelog (Jira Server)
                var issueWithChangelog = await jiraApi.GetIssueAsync(issueKey, "changelog");
                if (issueWithChangelog.Changelog != null)
                {
                    var events = ExtractEventsFromChangelog(issueWithChangelog.Changelog);

                    _logger.LogDebug(
                        "Retrieved {Count} history events via expand=changelog for {IssueKey} from {Hostname}",
                        events.Count,
                        issueKey,
                        hostname
                    );
                    return events;
                }
                else
                {
                    _logger.LogWarning(
                        "No changelog data available for {IssueKey} from {Hostname}",
                        issueKey,
                        hostname
                    );
                    return [];
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get Jira history for {IssueKey} from {Hostname}",
                issueKey,
                hostname
            );
            return [];
        }
    }

    /// <summary>
    /// Extract events from changelog response (common logic for both endpoints)
    /// </summary>
    private static List<IssueEvent> ExtractEventsFromChangelog(JiraChangelogResponse changelog)
    {
        var events = new List<IssueEvent>();

        foreach (var history in changelog.Histories)
        {
            foreach (var item in history.Items)
            {
                events.Add(
                    new IssueEvent
                    {
                        Id = $"{history.Id}_{item.Field}",
                        EventType = $"{item.Field}_changed",
                        Actor = history.Author.Name,
                        OccurredAt = history.Created,
                        Description = BuildChangeDescription(item),
                    }
                );
            }
        }

        return events;
    }

    /// <summary>
    /// Get issue details using REST API for any Jira instance
    /// </summary>
    public async Task<Issue?> GetIssueDetailsAsync(string hostname, string issueKey)
    {
        try
        {
            var jiraApi = GetOrCreateJiraApi(hostname);
            var issueResponse = await jiraApi.GetIssueAsync(issueKey);

            if (issueResponse?.Fields == null)
                return null;

            var issue = new Issue
            {
                Id = issueKey,
                Title = issueResponse.Fields.Summary ?? "",
                Description = issueResponse.Fields.Description ?? "",
                Status = BaseIssueTrackerClient.MapToUniversalStatus(
                    issueResponse.Fields.Status?.Name ?? "",
                    BugTrackingProvider.Jira
                ),
                Author =
                    issueResponse.Fields.Reporter?.DisplayName
                    ?? issueResponse.Fields.Reporter?.Name
                    ?? "",
                CreatedAt = issueResponse.Fields.Created,
                UpdatedAt = issueResponse.Fields.Updated,
                ClosedAt = issueResponse.Fields.ResolutionDate,
                Labels = issueResponse.Fields.Labels?.ToList() ?? [],
                Assignees =
                    issueResponse.Fields.Assignee != null
                        ?
                        [
                            issueResponse.Fields.Assignee.DisplayName
                                ?? issueResponse.Fields.Assignee.Name,
                        ]
                        : [],
                Priority = issueResponse.Fields.Priority?.Name ?? "",
                Severity = issueResponse.Fields.Priority?.Name ?? "", // Jira typically uses priority for severity
                Provider = BugTrackingProvider.Jira,
            };

            _logger.LogDebug(
                "Retrieved issue details via REST API for {IssueKey} from {Hostname}",
                issueKey,
                hostname
            );
            return issue;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get Jira issue details for {IssueKey} from {Hostname}",
                issueKey,
                hostname
            );
            return null;
        }
    }

    /// <summary>
    /// Get enhanced comments including worklog data
    /// </summary>
    public async Task<List<Comment>> GetEnhancedCommentsAsync(string hostname, string issueKey)
    {
        try
        {
            var jiraApi = GetOrCreateJiraApi(hostname);

            // Get both regular comments and worklog entries
            var commentsTask = jiraApi.GetCommentsAsync(issueKey);
            var worklogTask = jiraApi.GetWorklogAsync(issueKey);

            await Task.WhenAll(commentsTask, worklogTask);

            var comments = commentsTask.Result;
            var worklog = worklogTask.Result;

            var allComments = new List<Comment>();

            // Add regular comments
            foreach (var comment in comments.Comments)
            {
                allComments.Add(
                    new Comment
                    {
                        Id = comment.Id,
                        Author = comment.Author.Name,
                        Content = comment.Body,
                        CreatedAt = comment.Created,
                    }
                );
            }

            // Add worklog entries as special comments
            foreach (var work in worklog.Worklogs)
            {
                if (!string.IsNullOrWhiteSpace(work.Comment))
                {
                    allComments.Add(
                        new Comment
                        {
                            Id = $"worklog-{work.Id}",
                            Author = work.Author.Name,
                            Content = $"[WORKLOG: {work.TimeSpent}] {work.Comment}",
                            CreatedAt = work.Started,
                        }
                    );
                }
            }

            _logger.LogDebug(
                "Retrieved {CommentsCount} comments + {WorklogCount} worklog entries for {IssueKey}",
                comments.Comments.Length,
                worklog.Worklogs.Length,
                issueKey
            );

            return allComments.OrderBy(c => c.CreatedAt).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get enhanced comments for {IssueKey} from {Hostname}",
                issueKey,
                hostname
            );
            return [];
        }
    }

    private IJiraApi GetOrCreateJiraApi(string hostname)
    {
        return _jiraApiCache.GetOrAdd(hostname, CreateJiraApiForHost);
    }

    private IJiraApi CreateJiraApiForHost(string hostname)
    {
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.BaseAddress = new Uri($"https://{hostname}");

        // Create Refit client with the configured HttpClient that already has
        // rate limiting and retry policies from ServiceRegistration
        var refitSettings = new Refit.RefitSettings();
        return Refit.RestService.For<IJiraApi>(httpClient, refitSettings);
    }

    private static string BuildChangeDescription(JiraChangeItem item)
    {
        return item.Field switch
        {
            "status" => $"Status changed from '{item.FromString}' to '{item.ToValue}'",
            "assignee" =>
                $"Assignee changed from '{item.FromString ?? "Unassigned"}' to '{item.ToValue ?? "Unassigned"}'",
            "priority" => $"Priority changed from '{item.FromString}' to '{item.ToValue}'",
            "labels" => $"Labels changed: {item.ToValue}",
            "resolution" => $"Resolution changed from '{item.FromString}' to '{item.ToValue}'",
            "Fix Version/s" => $"Fix Version changed from '{item.FromString}' to '{item.ToValue}'",
            "Component/s" => $"Components changed: {item.ToValue}",
            _ => $"{item.Field} changed from '{item.FromString}' to '{item.ToValue}'",
        };
    }
}
