using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using DataCollection.Core.Models.IssueTracker;
using DataCollection.Infrastructure.Models;
using DataCollection.Infrastructure.Utilities;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

public class JiraAccessDeniedException : Exception
{
    public string IssueKey { get; }

    public JiraAccessDeniedException(string issueKey, string message)
        : base(message)
    {
        IssueKey = issueKey;
    }
}

public class JiraClient : BaseIssueTrackerClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<JiraClient> _logger;
    private readonly JiraHistoryService? _jiraHistoryService;
    private readonly ConcurrentDictionary<string, UniversalUserProfile> _userProfileCache = new();
    private readonly ConcurrentDictionary<string, Repository> _repositoryCache = new();

    public JiraClient(
        HttpClient httpClient,
        ILogger<JiraClient> logger,
        JiraHistoryService? jiraHistoryService = null
    )
    {
        _httpClient = httpClient;
        _logger = logger;
        _jiraHistoryService = jiraHistoryService;
    }

    public override BugTrackingProvider Provider => BugTrackingProvider.Jira;

    public override async Task<bool> IsValidRepositoryUrlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        try
        {
            using var response = await _httpClient.GetAsync(url);

            // Check for auth requirement
            if (
                response.StatusCode == HttpStatusCode.Found
                && response.Headers.Location?.ToString().Contains("login") == true
            )
            {
                return true; // Valid Jira, just requires auth
            }

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var metaTags = HtmlParsingUtilities.ExtractMetaTags(content);

                // Check for Jira-specific meta tags
                return metaTags.ContainsKey("application-name")
                    && metaTags["application-name"].ToLowerInvariant().Contains("jira");
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate Jira URL {Url}", url);
            return false;
        }
    }

    public override async Task<Repository?> GetRepositoryAsync(string identifier)
    {
        if (_repositoryCache.TryGetValue(identifier, out var cachedRepo))
            return cachedRepo;

        try
        {
            // For Jira, identifier is typically "hostname/projectkey"
            var parts = identifier.Split('/');
            if (parts.Length < 2)
                throw new ArgumentException($"Invalid Jira repository identifier: {identifier}");

            var hostname = parts[0];
            var projectKey = parts[1];
            var baseUrl = $"https://{hostname}";
            var projectUrl = $"{baseUrl}/browse/{projectKey}";

            using var response = await _httpClient.GetAsync(projectUrl);
            var content = await response.Content.ReadAsStringAsync();

            // Check for auth requirement
            if (IsAuthRequired(response, content))
            {
                _logger.LogWarning(
                    "Authentication required for Jira project {ProjectKey} at {Hostname}",
                    projectKey,
                    hostname
                );
                return null;
            }

            var repository = new Repository
            {
                Identifier = identifier,
                Name = $"{projectKey} Project",
                Description = $"Jira project {projectKey}",
                Url = projectUrl,
                Provider = BugTrackingProvider.Jira,
                ProviderSpecificData = new JiraRepositoryData
                {
                    ProjectKey = projectKey,
                    ProjectType = "software", // Default assumption
                    Lead = "", // Would need additional API call
                    Components = [], // Would need additional API call
                    Versions = [], // Would need additional API call
                },
            };

            _repositoryCache[identifier] = repository;
            return repository;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Jira repository {Identifier}", identifier);
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

            var hostname = parts[0];

            // Try enhanced REST API approach first if JiraHistoryService is available
            if (_jiraHistoryService != null)
            {
                try
                {
                    var restApiIssue = await _jiraHistoryService.GetIssueDetailsAsync(
                        hostname,
                        issueId
                    );
                    if (restApiIssue != null)
                    {
                        _logger.LogDebug(
                            "Retrieved issue {IssueId} via REST API from {Hostname}",
                            issueId,
                            hostname
                        );
                        return restApiIssue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "REST API failed for issue {IssueId}, falling back to HTML parsing",
                        issueId
                    );
                    // Fall through to HTML parsing fallback
                }
            }

            // Fallback to HTML parsing approach
            _logger.LogDebug(
                "Using HTML parsing fallback for issue {IssueId} from {Hostname}",
                issueId,
                hostname
            );

            var projectKey = parts[1];
            var baseUrl = $"https://{hostname}";
            var issueUrl = $"{baseUrl}/browse/{issueId}";

            using var response = await _httpClient.GetAsync(issueUrl);
            var content = await response.Content.ReadAsStringAsync();

            // Check for auth requirement
            if (IsAuthRequired(response, content))
            {
                throw new JiraAccessDeniedException(
                    issueId,
                    $"Authentication required to access issue {issueId}"
                );
            }

            var metaTags = HtmlParsingUtilities.ExtractMetaTags(content);
            var fields = HtmlParsingUtilities.ExtractIssueFields(content);

            var issue = new Issue
            {
                Id = issueId,
                Title = fields.GetValueOrDefault("summary-val", ""),
                Description = fields.GetValueOrDefault("description-val", ""),
                Status = MapToUniversalStatus(
                    fields.GetValueOrDefault("status-val", ""),
                    BugTrackingProvider.Jira
                ),
                Author = fields.GetValueOrDefault("reporter-val", ""),
                CreatedAt =
                    HtmlParsingUtilities.ParseJiraDate(ExtractDateFromHtml(content, "Created:"))
                    ?? DateTimeOffset.MinValue,
                UpdatedAt = HtmlParsingUtilities.ParseJiraDate(
                    ExtractDateFromHtml(content, "Updated:")
                ),
                ClosedAt = IsClosedStatus(fields.GetValueOrDefault("status-val", ""))
                    ? HtmlParsingUtilities.ParseJiraDate(ExtractDateFromHtml(content, "Resolved:"))
                    : null,
                Labels = ParseLabels(fields.GetValueOrDefault("labels-val", "")),
                Assignees = string.IsNullOrEmpty(fields.GetValueOrDefault("assignee-val", ""))
                    ? []
                    : [fields["assignee-val"]],
                Priority = fields.GetValueOrDefault("priority-val", ""),
                Severity = fields.GetValueOrDefault("priority-val", ""), // Jira uses priority as severity
                Provider = BugTrackingProvider.Jira,
                ProviderSpecificData = new JiraIssueData
                {
                    Key = issueId,
                    IssueType = "Bug", // Default assumption
                    Resolution = fields.GetValueOrDefault("resolution-val", ""),
                    Reporter = fields.GetValueOrDefault("reporter-val", ""),
                    Components = ParseLabels(fields.GetValueOrDefault("components-val", "")),
                    FixVersions = ParseLabels(fields.GetValueOrDefault("fixVersions-val", "")),
                    Environment = "", // Would need to be extracted from description or custom field
                    StoryPoints = null, // Would need to be extracted from custom field
                },
            };

            var reporter = issue.Author;
            if (!string.IsNullOrWhiteSpace(reporter))
                UpdateCachedProfileFromHtml(reporter, content);

            foreach (var a in issue.Assignees)
            {
                if (!string.IsNullOrWhiteSpace(a))
                    UpdateCachedProfileFromHtml(a, content);
            }

            return issue;
        }
        catch (JiraAccessDeniedException)
        {
            throw; // Re-throw access denied exceptions
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get Jira issue {IssueId} from repository {RepositoryIdentifier}",
                issueId,
                repositoryIdentifier
            );
            return null;
        }
    }

    public override Task<List<Issue>> SearchIssuesAsync(
        string repositoryIdentifier,
        IssueSearchQuery query
    )
    {
        try
        {
            // For now, return empty list as Jira search requires more complex JQL parsing
            _logger.LogWarning("Jira search not fully implemented yet");
            return Task.FromResult(new List<Issue>());
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to search Jira issues in repository {RepositoryIdentifier}",
                repositoryIdentifier
            );
            return Task.FromResult(new List<Issue>());
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

            var hostname = parts[0];

            // Use JiraHistoryService for enhanced comments (includes worklog) if available
            if (_jiraHistoryService != null)
            {
                try
                {
                    var enhancedComments = await _jiraHistoryService.GetEnhancedCommentsAsync(
                        hostname,
                        issueId
                    );
                    _logger.LogDebug(
                        "Retrieved {Count} enhanced comments for Jira issue {IssueId}",
                        enhancedComments.Count,
                        issueId
                    );
                    return enhancedComments;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to get enhanced comments, falling back to basic comments"
                    );
                    // Fall through to basic implementation
                }
            }

            // Fallback to basic comment retrieval
            var baseUrl = $"https://{hostname}";
            var commentsUrl = $"{baseUrl}/rest/api/2/issue/{issueId}/comment";

            using var response = await _httpClient.GetAsync(commentsUrl);
            var content = await response.Content.ReadAsStringAsync();

            // Check for auth requirement
            if (IsAuthRequired(response, content))
            {
                throw new JiraAccessDeniedException(
                    issueId,
                    $"Authentication required to access comments for issue {issueId}"
                );
            }

            // Parse JSON response
            var jsonDoc = JsonDocument.Parse(content);
            var comments = new List<Comment>();

            if (jsonDoc.RootElement.TryGetProperty("comments", out var commentsArray))
            {
                foreach (var commentElement in commentsArray.EnumerateArray())
                {
                    var author =
                        commentElement.GetProperty("author").GetProperty("name").GetString()
                        ?? "unknown";
                    var body = commentElement.GetProperty("body").GetString() ?? "";
                    var created = commentElement.GetProperty("created").GetString();

                    if (DateTime.TryParse(created, out var createdDate))
                    {
                        comments.Add(
                            new Comment
                            {
                                Author = author,
                                Content = body,
                                CreatedAt = createdDate,
                                Id = commentElement.GetProperty("id").GetString() ?? "",
                            }
                        );
                    }
                }
            }

            _logger.LogDebug(
                "Retrieved {Count} basic comments for Jira issue {IssueId}",
                comments.Count,
                issueId
            );
            return comments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Jira comments for issue {IssueId}", issueId);
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
            if (_jiraHistoryService == null)
            {
                _logger.LogWarning(
                    "Jira history service not available, falling back to empty events list"
                );
                return [];
            }

            var parts = repositoryIdentifier.Split('/');
            if (parts.Length < 2)
            {
                throw new ArgumentException(
                    $"Invalid Jira repository identifier: {repositoryIdentifier}"
                );
            }

            var hostname = parts[0];

            // Use JiraHistoryService with Refit, rate limiting, and retry policies
            var events = await _jiraHistoryService.GetIssueHistoryAsync(hostname, issueId);

            _logger.LogDebug(
                "Retrieved {Count} events for Jira issue {IssueId}",
                events.Count,
                issueId
            );
            return events;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Jira events for issue {IssueId}", issueId);
            return [];
        }
    }

    public override Task<UniversalUserProfile?> GetUserProfileAsync(string username)
    {
        // Return null for empty usernames - this will trigger fallback profile creation
        if (string.IsNullOrWhiteSpace(username))
        {
            _logger.LogDebug("Skipping user profile creation for empty username");
            return Task.FromResult<UniversalUserProfile?>(null);
        }

        if (_userProfileCache.TryGetValue(username, out var cachedProfile))
            return Task.FromResult<UniversalUserProfile?>(cachedProfile);

        try
        {
            // Create a basic profile with proper username
            var profile = new UniversalUserProfile
            {
                Username = username,
                Provider = BugTrackingProvider.Jira,
                ActivitySummary = "Requires JQL queries for metrics",
                ContributionMetrics = null,
                ProjectInvolvement = null,
                ActivityLevel = "Unknown",
            };

            // TODO: Future enhancement - role detection from Jira user pages or permissions
            // For now, basic profile with correct username is sufficient

            _userProfileCache[username] = profile;
            _logger.LogDebug("Created basic user profile for {Username}", username);
            return Task.FromResult<UniversalUserProfile?>(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Jira user profile for {Username}", username);
            return Task.FromResult<UniversalUserProfile?>(null);
        }
    }

    public override Task<List<string>> GetRepositoryContributorsAsync(string repositoryIdentifier)
    {
        try
        {
            _logger.LogWarning("Jira contributor listing not implemented yet");
            return Task.FromResult(new List<string>());
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get Jira contributors for repository {RepositoryIdentifier}",
                repositoryIdentifier
            );
            return Task.FromResult(new List<string>());
        }
    }

    public override Task<List<Repository>> SearchRepositoriesAsync(string query)
    {
        try
        {
            _logger.LogWarning("Jira repository search not implemented yet");
            return Task.FromResult(new List<Repository>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search Jira repositories with query {Query}", query);
            return Task.FromResult(new List<Repository>());
        }
    }

    public UniversalUserProfile ExtractRoleInformationFromPage(string html, string username)
    {
        var metaTags = HtmlParsingUtilities.ExtractMetaTags(html);
        var currentUser = HtmlParsingUtilities.GetCurrentUser(metaTags);

        var profile = new UniversalUserProfile
        {
            Username = username,
            Provider = BugTrackingProvider.Jira,
            ActivitySummary = "Extracted from profile page",
            ContributionMetrics = null,
            ProjectInvolvement = null,
            ActivityLevel = "Unknown",
        };

        if (!string.IsNullOrEmpty(html) && !string.IsNullOrEmpty(username))
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var userNodes =
                doc.DocumentNode.SelectNodes(
                        "//*[contains(@class,'user') or contains(@class,'people') or contains(@class,'aui-avatar')]"
                    )
                    ?.AsEnumerable()
                ?? Enumerable.Empty<HtmlNode>();

            foreach (var node in userNodes)
            {
                if (!node.InnerText.Contains(username, StringComparison.OrdinalIgnoreCase))
                    continue;

                var container = node;
                for (int i = 0; i < 3 && container.ParentNode != null; i++)
                    container = container.ParentNode;

                var badges = container
                    .Descendants()
                    .Where(n =>
                        n.GetAttributeValue("class", "")
                            .Contains("badge", StringComparison.OrdinalIgnoreCase)
                        || n.GetAttributeValue("class", "")
                            .Contains("role", StringComparison.OrdinalIgnoreCase)
                    );

                foreach (var badge in badges)
                {
                    var text = (badge.InnerText ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(text))
                        continue;

                    if (text.Contains("Administrator", StringComparison.OrdinalIgnoreCase))
                    {
                        profile.IsTriageOwner = true;
                        profile.IsDeveloper = true;
                        profile.RoleIndicators = "Jira Administrator";
                    }
                    else if (text.Contains("Developer", StringComparison.OrdinalIgnoreCase))
                    {
                        profile.IsDeveloper = true;
                        profile.RoleIndicators = string.IsNullOrEmpty(profile.RoleIndicators)
                            ? "Jira Developer"
                            : profile.RoleIndicators + "; Jira Developer";
                    }
                }
            }
        }

        if (currentUser == username || string.IsNullOrEmpty(currentUser))
        {
            var isAdmin = HtmlParsingUtilities.IsUserAdmin(metaTags);
            if (isAdmin)
            {
                profile.IsTriageOwner = true;
                profile.IsDeveloper = true;
                profile.RoleIndicators = string.IsNullOrEmpty(profile.RoleIndicators)
                    ? "Jira Administrator"
                    : profile.RoleIndicators + "; Jira Administrator";
            }
        }

        return profile;
    }

    private bool IsAuthRequired(HttpResponseMessage response, string content)
    {
        return response.StatusCode == HttpStatusCode.Found
                && response.Headers.Location?.ToString().Contains("login") == true
            || HtmlParsingUtilities.IsAuthenticationRequired(content, (int)response.StatusCode);
    }

    private static List<string> ParseLabels(string labelsText)
    {
        if (string.IsNullOrEmpty(labelsText))
            return [];

        return labelsText
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .ToList();
    }

    private void UpdateCachedProfileFromHtml(string username, string html)
    {
        if (string.IsNullOrWhiteSpace(username))
            return;

        var extracted = ExtractRoleInformationFromPage(html, username);
        if (extracted == null)
            return;

        if (!_userProfileCache.TryGetValue(username, out var existing))
        {
            _userProfileCache[username] = extracted;
            return;
        }

        existing.IsMaintainer =
            existing.IsMaintainer == true || extracted.IsMaintainer == true
                ? true
                : existing.IsMaintainer;
        existing.IsCommitter =
            existing.IsCommitter == true || extracted.IsCommitter == true
                ? true
                : existing.IsCommitter;
        existing.IsTriageOwner =
            existing.IsTriageOwner == true || extracted.IsTriageOwner == true
                ? true
                : existing.IsTriageOwner;
        existing.IsDeveloper =
            existing.IsDeveloper == true || extracted.IsDeveloper == true
                ? true
                : existing.IsDeveloper;

        if (!string.IsNullOrEmpty(extracted.RoleIndicators))
        {
            existing.RoleIndicators = string.IsNullOrEmpty(existing.RoleIndicators)
                ? extracted.RoleIndicators
                : existing.RoleIndicators + "; " + extracted.RoleIndicators;
        }
    }

    private static string ExtractDateFromHtml(string html, string dateLabel)
    {
        var pattern = $@"{dateLabel}\s*([^<\n\r]+)";
        var match = System.Text.RegularExpressions.Regex.Match(
            html,
            pattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );

        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private static bool IsClosedStatus(string status)
    {
        var closedStatuses = new[] { "closed", "done", "resolved", "complete", "finished" };
        return closedStatuses.Any(s => status.ToLowerInvariant().Contains(s));
    }
}
