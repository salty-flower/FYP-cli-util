using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DataCollection.Core.Models.IssueTracker;
using DataCollection.Infrastructure.Models;
using DataCollection.Infrastructure.Utilities;
using Microsoft.Extensions.Logging;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

public class TracClient : BaseIssueTrackerClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TracClient> _logger;
    private readonly ConcurrentDictionary<string, UniversalUserProfile> _userProfileCache = new();
    private readonly ConcurrentDictionary<string, Repository> _repositoryCache = new();

    public TracClient(HttpClient httpClient, ILogger<TracClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public override BugTrackingProvider Provider => BugTrackingProvider.Trac;

    public override async Task<bool> IsValidRepositoryUrlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        try
        {
            using var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();

                // Check for Trac-specific indicators
                return content.Contains("trac.css")
                    || content.Contains("trac.js")
                    || content.Contains("class=\"trac-")
                    || content.Contains("/wiki/TracGuide");
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate Trac URL {Url}", url);
            return false;
        }
    }

    public override async Task<Repository?> GetRepositoryAsync(string identifier)
    {
        if (_repositoryCache.TryGetValue(identifier, out var cachedRepo))
            return cachedRepo;

        try
        {
            // For Trac, identifier is typically the base URL
            var baseUrl = identifier.TrimEnd('/');

            using var response = await _httpClient.GetAsync(baseUrl);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to access Trac instance at {Url}", baseUrl);
                return null;
            }

            // Extract project name from page title or content
            var titleMatch = Regex.Match(
                content,
                @"<title>([^<]+)</title>",
                RegexOptions.IgnoreCase
            );
            var projectName = titleMatch.Success
                ? titleMatch.Groups[1].Value.Trim()
                : "Trac Project";

            var repository = new Repository
            {
                Identifier = identifier,
                Name = projectName,
                Description = $"Trac issue tracker at {baseUrl}",
                Url = baseUrl,
                Provider = BugTrackingProvider.Trac,
                ProviderSpecificData = new TracRepositoryData
                {
                    BaseUrl = baseUrl,
                    ProjectName = ExtractProjectName(content),
                },
            };

            _repositoryCache[identifier] = repository;
            return repository;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Trac repository {Identifier}", identifier);
            return null;
        }
    }

    public override async Task<Issue?> GetIssueAsync(string repositoryIdentifier, string issueId)
    {
        try
        {
            var baseUrl = repositoryIdentifier.TrimEnd('/');
            var ticketUrl = $"{baseUrl}/ticket/{issueId}";

            using var response = await _httpClient.GetAsync(ticketUrl);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to access Trac ticket {IssueId} at {Url}",
                    issueId,
                    ticketUrl
                );
                return null;
            }

            // Extract structured data from JavaScript old_values object
            var oldValuesMatch = Regex.Match(
                content,
                @"var\s+old_values\s*=\s*(\{[^;]+\});",
                RegexOptions.Singleline
            );
            if (!oldValuesMatch.Success)
            {
                _logger.LogWarning(
                    "Could not find old_values data in Trac ticket {IssueId}",
                    issueId
                );
                return null;
            }

            var oldValuesJson = oldValuesMatch.Groups[1].Value;
            var ticketData = JsonSerializer.Deserialize<TracTicketData>(
                oldValuesJson,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
            );

            if (ticketData == null)
            {
                _logger.LogWarning("Failed to deserialize ticket data for {IssueId}", issueId);
                return null;
            }

            // Parse dates
            var createdAt = DateTimeOffset.TryParse(ticketData.Time, out var created)
                ? created
                : DateTimeOffset.MinValue;
            var updatedAt = DateTimeOffset.TryParse(ticketData.Changetime, out var updated)
                ? updated
                : createdAt;
            var closedAt = ticketData.Status == "closed" ? (DateTimeOffset?)updatedAt : null;

            return new Issue
            {
                Id = issueId,
                Title = ticketData.Summary ?? "",
                Description = ExtractDescription(content),
                Status = MapToUniversalStatus(ticketData.Status ?? "", BugTrackingProvider.Trac),
                Author = ticketData.Reporter ?? "",
                CreatedAt = createdAt,
                UpdatedAt = updatedAt,
                ClosedAt = closedAt,
                Labels = ParseKeywords(ticketData.Keywords),
                Assignees =
                    string.IsNullOrEmpty(ticketData.Owner) || ticketData.Owner == "nobody"
                        ? []
                        : [ticketData.Owner],
                Priority = ticketData.Priority ?? "",
                Severity = ticketData.Severity ?? "",
                Provider = BugTrackingProvider.Trac,
                ProviderSpecificData = new TracIssueData
                {
                    Component = ticketData.Component ?? "",
                    Type = ticketData.Type ?? "",
                    Stage = ticketData.Stage ?? "",
                    Resolution = ticketData.Resolution ?? "",
                    Version = ticketData.Version ?? "",
                    HasPatch = ticketData.Has_patch == "1",
                    NeedsTests = ticketData.Needs_tests == "1",
                    NeedsDocs = ticketData.Needs_docs == "1",
                    Easy = ticketData.Easy == "1",
                    UiUx = ticketData.Ui_ux == "1",
                },
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get Trac issue {IssueId} from repository {RepositoryIdentifier}",
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
            // Trac search would require complex query parsing - not implemented yet
            _logger.LogWarning("Trac search not implemented yet");
            return Task.FromResult(new List<Issue>());
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to search Trac issues in repository {RepositoryIdentifier}",
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
            var baseUrl = repositoryIdentifier.TrimEnd('/');
            var ticketUrl = $"{baseUrl}/ticket/{issueId}";

            using var response = await _httpClient.GetAsync(ticketUrl);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var comments = new List<Comment>();

            // Extract comments from JavaScript 'changes' array (Django Trac format)
            var changesMatch = Regex.Match(
                content,
                @"var\s+changes\s*=\s*(\[.*?\]);\s",
                RegexOptions.Singleline
            );
            if (changesMatch.Success)
            {
                try
                {
                    var changesJson = changesMatch.Groups[1].Value;
                    var changes = JsonSerializer.Deserialize<TracChange[]>(changesJson);

                    foreach (var change in changes)
                    {
                        if (!string.IsNullOrEmpty(change.Comment))
                        {
                            // Convert Unix timestamp to DateTimeOffset
                            var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(
                                (long)(change.Date / 1000)
                            );

                            comments.Add(
                                new Comment
                                {
                                    Id = change.Cnum.ToString(),
                                    Author = change.Author,
                                    Content = CleanTracComment(change.Comment),
                                    CreatedAt = createdAt,
                                    ProviderSpecificData = new TracCommentData
                                    {
                                        ChangeNumber = change.Cnum,
                                        IsPermanent = change.Permanent == 1,
                                    },
                                }
                            );
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to parse Trac changes JSON, falling back to HTML parsing"
                    );
                    // Fall back to HTML parsing for non-Django Trac instances
                }
            }

            // Fallback: Extract comments from HTML (for older Trac versions)
            if (comments.Count == 0)
            {
                var changePattern =
                    @"<h3\s+class=""change""\s+id=""comment:(\d+)""[^>]*>.*?by\s+<span\s+class=""trac-author"">([^<]+)</span>.*?<a\s+class=""timeline""[^>]+title=""[^""]*?(\d{4}-\d{2}-\d{2}[^""]*?)""[^>]*>.*?<div\s+class=""comment\s+searchable""[^>]*>(.*?)(?=<h3\s+class=""change""|<div\s+class=""trac-help""|$)";
                var changeMatches = Regex.Matches(
                    content,
                    changePattern,
                    RegexOptions.Singleline | RegexOptions.IgnoreCase
                );

                foreach (Match match in changeMatches)
                {
                    var commentId = match.Groups[1].Value;
                    var author = match.Groups[2].Value.Trim();
                    var dateStr = match.Groups[3].Value.Trim();
                    var commentHtml = match.Groups[4].Value;

                    // Extract text content from HTML
                    var commentText = ExtractTextFromHtml(commentHtml);
                    if (string.IsNullOrWhiteSpace(commentText))
                        continue;

                    // Parse date
                    var createdAt = TryParseTracDate(dateStr);

                    comments.Add(
                        new Comment
                        {
                            Id = commentId,
                            Author = author,
                            Content = commentText.Trim(),
                            CreatedAt = createdAt,
                            Provider = BugTrackingProvider.Trac,
                        }
                    );
                }
            }

            _logger.LogDebug(
                "Retrieved {Count} comments for Trac issue {IssueId}",
                comments.Count,
                issueId
            );
            return comments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Trac comments for issue {IssueId}", issueId);
            return [];
        }
    }

    public override Task<List<IssueEvent>> GetIssueEventsAsync(
        string repositoryIdentifier,
        string issueId
    )
    {
        try
        {
            // Trac events would require parsing change history - basic implementation
            _logger.LogWarning("Trac events extraction not fully implemented yet");
            return Task.FromResult(new List<IssueEvent>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Trac events for issue {IssueId}", issueId);
            return Task.FromResult(new List<IssueEvent>());
        }
    }

    public override async Task<UniversalUserProfile?> GetUserProfileAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username) || username == "nobody")
        {
            _logger.LogDebug("Skipping user profile creation for empty/nobody username");
            return null;
        }

        if (_userProfileCache.TryGetValue(username, out var cachedProfile))
            return cachedProfile;

        try
        {
            // Try to get user information with role detection
            var profile = await CreateTracUserProfileAsync(username);

            _userProfileCache[username] = profile;
            _logger.LogDebug(
                "Created Trac user profile for {Username} with roles: {Roles}",
                username,
                profile.RoleIndicators ?? "none"
            );
            return profile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Trac user profile for {Username}", username);
            return null;
        }
    }

    public override Task<List<string>> GetRepositoryContributorsAsync(string repositoryIdentifier)
    {
        try
        {
            _logger.LogWarning("Trac contributor listing not implemented yet");
            return Task.FromResult(new List<string>());
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get Trac contributors for repository {RepositoryIdentifier}",
                repositoryIdentifier
            );
            return Task.FromResult(new List<string>());
        }
    }

    public override Task<List<Repository>> SearchRepositoriesAsync(string query)
    {
        try
        {
            _logger.LogWarning("Trac repository search not implemented yet");
            return Task.FromResult(new List<Repository>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search Trac repositories with query {Query}", query);
            return Task.FromResult(new List<Repository>());
        }
    }

    private static string ExtractProjectName(string html)
    {
        // Try to extract from main heading or title
        var headingMatch = Regex.Match(html, @"<h1[^>]*>([^<]+)</h1>", RegexOptions.IgnoreCase);
        if (headingMatch.Success)
            return headingMatch.Groups[1].Value.Trim();

        return "Trac Project";
    }

    private static string ExtractDescription(string html)
    {
        // Extract the main description from the first searchable div
        var descMatch = Regex.Match(
            html,
            @"<div\s+class=""searchable""[^>]*>\s*<p>(.*?)</p>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase
        );

        if (descMatch.Success)
        {
            return ExtractTextFromHtml(descMatch.Groups[1].Value).Trim();
        }

        return "";
    }

    private static string ExtractTextFromHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";

        // Remove HTML tags and decode entities
        var text = Regex.Replace(html, @"<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ");
        return text.Trim();
    }

    private static string CleanTracComment(string comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return "";

        // Decode HTML entities and clean up formatting
        var cleaned = System.Net.WebUtility.HtmlDecode(comment);
        cleaned = Regex.Replace(cleaned, @"\r\n", "\n");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        return cleaned;
    }

    private static List<string> ParseKeywords(string? keywords)
    {
        if (string.IsNullOrWhiteSpace(keywords))
            return [];

        return keywords
            .Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.Trim())
            .Where(k => !string.IsNullOrEmpty(k))
            .ToList();
    }

    private static DateTimeOffset TryParseTracDate(string dateStr)
    {
        // Trac dates can be in various formats
        var formats = new[]
        {
            "MMM d, yyyy, h:mm:ss tt",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ssZ",
            "yyyy-MM-ddTHH:mm:ss.fffZ",
        };

        foreach (var format in formats)
        {
            if (
                DateTimeOffset.TryParseExact(
                    dateStr,
                    format,
                    null,
                    System.Globalization.DateTimeStyles.None,
                    out var date
                )
            )
            {
                return date;
            }
        }

        return DateTimeOffset.MinValue;
    }

    /// <summary>
    /// Create a comprehensive Trac user profile with role detection
    /// </summary>
    private async Task<UniversalUserProfile> CreateTracUserProfileAsync(string username)
    {
        var profile = new UniversalUserProfile
        {
            Username = username,
            Provider = BugTrackingProvider.Trac,
            ActivitySummary = "Metrics not yet implemented", // Would need queries to populate
            ContributionMetrics = null,
            ProjectInvolvement = null,
            ActivityLevel = "Unknown",
        };

        try
        {
            // Attempt to fetch user-specific page for role information
            await ExtractTracUserRolesAsync(profile, username);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Could not extract roles for Trac user {Username}, using basic profile",
                username
            );
        }

        return profile;
    }

    /// <summary>
    /// Extract role and authority information for Trac user
    /// </summary>
    private async Task ExtractTracUserRolesAsync(UniversalUserProfile profile, string username)
    {
        var roleIndicators = new List<string>();

        // Try to get admin page or user information (if accessible)
        try
        {
            // Skip admin page checking for now - would need base URL context
            // In a real implementation, this would extract base URL from repository context
            // var adminUrl = $"{baseUrl}/admin/general/perm";
            // var response = await _httpClient.GetAsync(adminUrl);

            var response = (HttpResponseMessage?)null;

            if (response?.IsSuccessStatusCode == true)
            {
                var html = await response.Content.ReadAsStringAsync();
                var userRoles = ExtractUserRolesFromPermissionPage(html, username);

                if (userRoles.Contains("TRAC_ADMIN"))
                {
                    profile.IsMaintainer = true;
                    profile.IsDeveloper = true;
                    roleIndicators.Add("TRAC_ADMIN");
                }

                if (
                    userRoles.Any(r =>
                        r.Contains("PERMISSION_ADMIN")
                        || r.Contains("PERMISSION_GRANT")
                        || r.Contains("PERMISSION_REVOKE")
                    )
                )
                {
                    profile.IsTriageOwner = true;
                    profile.IsDeveloper = true;
                    roleIndicators.Add("Permission Admin");
                }

                if (
                    userRoles.Any(r =>
                        r.Contains("TICKET_") || r.Contains("MILESTONE_") || r.Contains("WIKI_")
                    )
                )
                {
                    profile.IsDeveloper = true;
                    roleIndicators.Add("Developer Permissions");
                }

                // Check for membership in jira-administrators equivalent
                if (userRoles.Any(r => r.Contains("administrator")))
                {
                    profile.IsMaintainer = true;
                    roleIndicators.Add("Administrator Group");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Could not access Trac admin permissions for {Username}",
                username
            );
        }

        // Note: Removed username pattern assumptions - only use explicit Trac indicators above

        if (roleIndicators.Count > 0)
        {
            profile.RoleIndicators = string.Join("; ", roleIndicators);
        }
    }

    /// <summary>
    /// Extract user roles from Trac permission admin page HTML
    /// </summary>
    private static List<string> ExtractUserRolesFromPermissionPage(string html, string username)
    {
        var roles = new List<string>();

        // Look for permission table rows containing the username
        var userRowPattern = $@"<tr[^>]*>.*?{Regex.Escape(username)}.*?</tr>";
        var matches = Regex.Matches(
            html,
            userRowPattern,
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        foreach (Match match in matches)
        {
            var rowHtml = match.Value;

            // Extract permission names from the row
            var permissionMatches = Regex.Matches(
                rowHtml,
                @">(TRAC_\w+|PERMISSION_\w+|TICKET_\w+|MILESTONE_\w+|WIKI_\w+)<"
            );
            foreach (Match permMatch in permissionMatches)
            {
                roles.Add(permMatch.Groups[1].Value);
            }
        }

        return roles;
    }

    /// <summary>
    /// Extract explicit role information from Trac platform indicators only
    /// </summary>
    private static void ExtractExplicitTracRoles(
        UniversalUserProfile profile,
        string username,
        List<string> roleIndicators,
        string pageContent
    )
    {
        // Only detect roles from explicit Trac platform indicators
        // No assumptions based on username patterns or activity

        // TODO: Research and implement actual Trac role indicators
        // Examples to look for: admin badges, permission lists, role labels
    }
}

// Data models for deserializing Trac JavaScript data
internal record TracTicketData(
    int Id,
    string? Summary,
    string? Status,
    string? Resolution,
    string? Reporter,
    string? Owner,
    string? Component,
    string? Type,
    string? Priority,
    string? Severity,
    string? Keywords,
    string? Version,
    string? Stage,
    string? Time,
    string? Changetime,
    string? Description,
    string? Has_patch,
    string? Needs_tests,
    string? Needs_docs,
    string? Easy,
    string? Ui_ux
);

// Model for Trac comment changes from JavaScript
internal record TracChange
{
    [JsonPropertyName("author")]
    public string Author { get; init; } = string.Empty;

    [JsonPropertyName("cnum")]
    public int Cnum { get; init; }

    [JsonPropertyName("comment")]
    public string Comment { get; init; } = string.Empty;

    [JsonPropertyName("date")]
    public double Date { get; init; }

    [JsonPropertyName("permanent")]
    public int Permanent { get; init; }
}
