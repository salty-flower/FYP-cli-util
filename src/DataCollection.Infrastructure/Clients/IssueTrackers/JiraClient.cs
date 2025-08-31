using System.Collections.Concurrent;
using System.Net;
using DataCollection.Core.Models.IssueTracker;
using DataCollection.Infrastructure.Models;
using DataCollection.Infrastructure.Utilities;
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
    private readonly ConcurrentDictionary<string, UniversalUserProfile> _userProfileCache = new();
    private readonly ConcurrentDictionary<string, Repository> _repositoryCache = new();

    public JiraClient(HttpClient httpClient, ILogger<JiraClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
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

            return new Issue
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

    public override async Task<List<Issue>> SearchIssuesAsync(
        string repositoryIdentifier,
        IssueSearchQuery query
    )
    {
        try
        {
            // For now, return empty list as Jira search requires more complex JQL parsing
            _logger.LogWarning("Jira search not fully implemented yet");
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to search Jira issues in repository {RepositoryIdentifier}",
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

            var hostname = parts[0];
            var baseUrl = $"https://{hostname}";
            var issueUrl = $"{baseUrl}/browse/{issueId}";

            using var response = await _httpClient.GetAsync(issueUrl);
            var content = await response.Content.ReadAsStringAsync();

            // Check for auth requirement
            if (IsAuthRequired(response, content))
            {
                _logger.LogWarning(
                    "Authentication required to access comments for issue {IssueId}",
                    issueId
                );
                return [];
            }

            // For now, return empty list as comment parsing requires more complex HTML analysis
            _logger.LogWarning("Jira comment parsing not fully implemented yet");
            return [];
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
            // For now, return empty list as history parsing requires more complex HTML analysis
            _logger.LogWarning("Jira history parsing not fully implemented yet");
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Jira events for issue {IssueId}", issueId);
            return [];
        }
    }

    public override async Task<UniversalUserProfile?> GetUserProfileAsync(string username)
    {
        if (_userProfileCache.TryGetValue(username, out var cachedProfile))
            return cachedProfile;

        try
        {
            // Create a basic profile for now - user profile detection would require accessing user pages
            var profile = new UniversalUserProfile
            {
                Username = username,
                Provider = BugTrackingProvider.Jira,
                TotalIssuesOpened = 0, // Would need JQL queries
                TotalIssuesAssigned = 0, // Would need JQL queries
                TotalCommentsPosted = 0, // Would need complex parsing
                ActivityScore = 0.0,
            };

            // Note: Role detection from HTML meta tags would happen during issue parsing
            // when we have access to the current user's permissions

            _userProfileCache[username] = profile;
            return profile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Jira user profile for {Username}", username);
            return null;
        }
    }

    public override async Task<List<string>> GetRepositoryContributorsAsync(
        string repositoryIdentifier
    )
    {
        try
        {
            _logger.LogWarning("Jira contributor listing not implemented yet");
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get Jira contributors for repository {RepositoryIdentifier}",
                repositoryIdentifier
            );
            return [];
        }
    }

    public override async Task<List<Repository>> SearchRepositoriesAsync(string query)
    {
        try
        {
            _logger.LogWarning("Jira repository search not implemented yet");
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search Jira repositories with query {Query}", query);
            return [];
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
            TotalIssuesOpened = 0,
            TotalIssuesAssigned = 0,
            TotalCommentsPosted = 0,
            ActivityScore = 0.0,
        };

        // Only extract admin role if this is the current user's profile
        if (currentUser == username || string.IsNullOrEmpty(currentUser))
        {
            var isAdmin = HtmlParsingUtilities.IsUserAdmin(metaTags);
            if (isAdmin)
            {
                profile.IsTriageOwner = true;
                profile.IsDeveloper = true;
                profile.RoleIndicators = "Jira Administrator";
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
