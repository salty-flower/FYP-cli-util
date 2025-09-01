using System.Text.Json.Serialization;
using DataCollection.Infrastructure.Json;
using Refit;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

/// <summary>
/// Refit interface for Jira REST API v2
/// Follows the existing GitHub API pattern with proper error handling and rate limiting
/// </summary>
[Headers("User-Agent: DataCollection-IssueTracker/1.0")]
public interface IJiraApi
{
    /// <summary>
    /// Get issue details with optional field expansion
    /// </summary>
    [Get("/rest/api/2/issue/{issueKey}")]
    Task<JiraIssueResponse> GetIssueAsync(
        string issueKey,
        [Query] string? expand = "changelog,transitions,worklog,attachments"
    );

    /// <summary>
    /// Get issue comments with pagination
    /// </summary>
    [Get("/rest/api/2/issue/{issueKey}/comment")]
    Task<JiraCommentsResponse> GetCommentsAsync(
        string issueKey,
        [Query] int startAt = 0,
        [Query] int maxResults = 50
    );

    /// <summary>
    /// Get issue changelog (history of all changes)
    /// </summary>
    [Get("/rest/api/2/issue/{issueKey}/changelog")]
    Task<JiraChangelogResponse> GetChangelogAsync(
        string issueKey,
        [Query] int startAt = 0,
        [Query] int maxResults = 100
    );

    /// <summary>
    /// Get available transitions for an issue
    /// </summary>
    [Get("/rest/api/2/issue/{issueKey}/transitions")]
    Task<JiraTransitionsResponse> GetTransitionsAsync(string issueKey);

    /// <summary>
    /// Get issue worklog entries
    /// </summary>
    [Get("/rest/api/2/issue/{issueKey}/worklog")]
    Task<JiraWorklogResponse> GetWorklogAsync(
        string issueKey,
        [Query] int startAt = 0,
        [Query] int maxResults = 50
    );

    /// <summary>
    /// Get project information
    /// </summary>
    [Get("/rest/api/2/project/{projectKey}")]
    Task<JiraProjectResponse> GetProjectAsync(string projectKey);
}

// Response DTOs matching Jira REST API v2 structure
public record JiraIssueResponse(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("fields")] JiraIssueFields Fields,
    [property: JsonPropertyName("changelog")] JiraChangelogResponse? Changelog = null
);

public record JiraIssueFields(
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("status")] JiraStatus Status,
    [property: JsonPropertyName("priority")] JiraPriority? Priority,
    [property: JsonPropertyName("creator")] JiraUser Creator,
    [property: JsonPropertyName("reporter")] JiraUser Reporter,
    [property: JsonPropertyName("assignee")] JiraUser? Assignee,
    [property: JsonPropertyName("created"), JsonConverter(typeof(JiraDateTimeConverter))]
        DateTime Created,
    [property: JsonPropertyName("updated"), JsonConverter(typeof(JiraDateTimeConverter))]
        DateTime Updated,
    [property:
        JsonPropertyName("resolutiondate"),
        JsonConverter(typeof(NullableJiraDateTimeConverter))
    ]
        DateTime? ResolutionDate,
    [property: JsonPropertyName("labels")] string[] Labels,
    [property: JsonPropertyName("components")] JiraComponent[] Components
);

public record JiraStatus(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("statusCategory")] JiraStatusCategory StatusCategory
);

public record JiraStatusCategory(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string Name
);

public record JiraPriority(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("id")] string Id
);

public record JiraUser(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("active")] bool Active
);

public record JiraComponent(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description
);

public record JiraCommentsResponse(
    [property: JsonPropertyName("startAt")] int StartAt,
    [property: JsonPropertyName("maxResults")] int MaxResults,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("comments")] JiraComment[] Comments
);

public record JiraComment(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("author")] JiraUser Author,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("created"), JsonConverter(typeof(JiraDateTimeConverter))]
        DateTime Created,
    [property: JsonPropertyName("updated"), JsonConverter(typeof(JiraDateTimeConverter))]
        DateTime Updated
);

public record JiraChangelogResponse(
    [property: JsonPropertyName("startAt")] int StartAt,
    [property: JsonPropertyName("maxResults")] int MaxResults,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("histories")] JiraHistoryItem[] Histories
);

public record JiraHistoryItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("author")] JiraUser Author,
    [property: JsonPropertyName("created"), JsonConverter(typeof(JiraDateTimeConverter))]
        DateTime Created,
    [property: JsonPropertyName("items")] JiraChangeItem[] Items
);

public record JiraChangeItem(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("fieldtype")] string FieldType,
    [property: JsonPropertyName("from")] string? From,
    [property: JsonPropertyName("fromString")] string? FromString,
    [property: JsonPropertyName("to")] string? To,
    [property: JsonPropertyName("toString")] string? ToValue
);

public record JiraTransitionsResponse(
    [property: JsonPropertyName("transitions")] JiraTransition[] Transitions
);

public record JiraTransition(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("to")] JiraStatus To
);

public record JiraWorklogResponse(
    [property: JsonPropertyName("startAt")] int StartAt,
    [property: JsonPropertyName("maxResults")] int MaxResults,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("worklogs")] JiraWorklogItem[] Worklogs
);

public record JiraWorklogItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("author")] JiraUser Author,
    [property: JsonPropertyName("comment")] string? Comment,
    [property: JsonPropertyName("started"), JsonConverter(typeof(JiraDateTimeConverter))]
        DateTime Started,
    [property: JsonPropertyName("timeSpent")] string TimeSpent,
    [property: JsonPropertyName("timeSpentSeconds")] int TimeSpentSeconds
);

public record JiraProjectResponse(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("projectTypeKey")] string ProjectTypeKey
);
