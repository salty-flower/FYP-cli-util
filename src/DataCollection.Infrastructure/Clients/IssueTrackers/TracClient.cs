using System.Collections.Concurrent;
using System.Text.Json;
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

            // Extract comments from change history
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

    public override Task<UniversalUserProfile?> GetUserProfileAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username) || username == "nobody")
        {
            _logger.LogDebug("Skipping user profile creation for empty/nobody username");
            return Task.FromResult<UniversalUserProfile?>(null);
        }

        if (_userProfileCache.TryGetValue(username, out var cachedProfile))
            return Task.FromResult<UniversalUserProfile?>(cachedProfile);

        try
        {
            // Create basic profile - Trac doesn't typically have detailed user pages
            var profile = new UniversalUserProfile
            {
                Username = username,
                Provider = BugTrackingProvider.Trac,
                TotalIssuesOpened = 0,
                TotalIssuesAssigned = 0,
                TotalCommentsPosted = 0,
                ActivityScore = 0.0,
            };

            _userProfileCache[username] = profile;
            _logger.LogDebug("Created basic user profile for Trac user {Username}", username);
            return Task.FromResult<UniversalUserProfile?>(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Trac user profile for {Username}", username);
            return Task.FromResult<UniversalUserProfile?>(null);
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
