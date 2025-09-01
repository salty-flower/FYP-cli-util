using System.Text.Json.Serialization;
using DataCollection.Infrastructure.Json;
using Refit;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

/// <summary>
/// Refit interface for Bugzilla REST API
/// Follows the same pattern as Jira API with proper error handling and rate limiting
/// </summary>
[Headers("User-Agent: DataCollection-IssueTracker/1.0")]
public interface IBugzillaApi
{
    /// <summary>
    /// Get Bugzilla version information (to validate instance)
    /// </summary>
    [Get("/rest/version")]
    Task<BugzillaVersionResponse> GetVersionAsync();

    /// <summary>
    /// Get bug details by ID
    /// </summary>
    [Get("/rest/bug/{bugId}")]
    Task<BugzillaBugResponse> GetBugAsync(int bugId);

    /// <summary>
    /// Search bugs with query parameters
    /// </summary>
    [Get("/rest/bug")]
    Task<BugzillaBugSearchResponse> SearchBugsAsync(
        [Query] string? product = null,
        [Query] string? status = null,
        [Query] string? creator = null,
        [Query] string? assigned_to = null,
        [Query] string? quicksearch = null,
        [Query] string? creation_time = null,
        [Query] string? last_change_time = null,
        [Query] int? limit = null,
        [Query] int? offset = null
    );

    /// <summary>
    /// Get comments for a specific bug
    /// </summary>
    [Get("/rest/bug/{bugId}/comment")]
    Task<BugzillaCommentsResponse> GetCommentsAsync(int bugId);

    /// <summary>
    /// Get history/changelog for a specific bug
    /// </summary>
    [Get("/rest/bug/{bugId}/history")]
    Task<BugzillaHistoryResponse> GetHistoryAsync(int bugId);

    /// <summary>
    /// Get available products (repositories)
    /// </summary>
    [Get("/rest/product")]
    Task<BugzillaProductsResponse> GetProductsAsync([Query] string? names = null);

    /// <summary>
    /// Get product details by ID
    /// </summary>
    [Get("/rest/product/{productId}")]
    Task<BugzillaProductResponse> GetProductAsync(int productId);
}

// Response DTOs matching Bugzilla REST API structure
public record BugzillaVersionResponse([property: JsonPropertyName("version")] string Version);

public record BugzillaBugResponse([property: JsonPropertyName("bugs")] BugzillaBug[] Bugs);

public record BugzillaBugSearchResponse([property: JsonPropertyName("bugs")] BugzillaBug[] Bugs);

public record BugzillaBug(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("resolution")] string? Resolution,
    [property: JsonPropertyName("priority")] string Priority,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("creator")] string Creator,
    [property: JsonPropertyName("creator_detail")] BugzillaUser? CreatorDetail,
    [property: JsonPropertyName("assigned_to")] string AssignedTo,
    [property: JsonPropertyName("assigned_to_detail")] BugzillaUser? AssignedToDetail,
    [property: JsonPropertyName("creation_time"), JsonConverter(typeof(JiraDateTimeConverter))]
        DateTime CreationTime,
    [property: JsonPropertyName("last_change_time"), JsonConverter(typeof(JiraDateTimeConverter))]
        DateTime LastChangeTime,
    [property: JsonPropertyName("product")] string Product,
    [property: JsonPropertyName("component")] string Component,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("target_milestone")] string? TargetMilestone,
    [property: JsonPropertyName("keywords")] string[] Keywords,
    [property: JsonPropertyName("op_sys")] string? OperatingSystem,
    [property: JsonPropertyName("platform")] string? Platform,
    [property: JsonPropertyName("classification")] string? Classification
);

public record BugzillaUser(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("real_name")] string? RealName,
    [property: JsonPropertyName("nick")] string? Nick
);

public record BugzillaCommentsResponse(
    [property: JsonPropertyName("bugs")] Dictionary<string, BugzillaBugComments> Bugs
);

public record BugzillaBugComments(
    [property: JsonPropertyName("comments")] BugzillaComment[] Comments
);

public record BugzillaComment(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("creator")] string Creator,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("time"), JsonConverter(typeof(JiraDateTimeConverter))]
        DateTime Time,
    [property: JsonPropertyName("creation_time"), JsonConverter(typeof(JiraDateTimeConverter))]
        DateTime CreationTime
);

public record BugzillaHistoryResponse(
    [property: JsonPropertyName("bugs")] BugzillaHistoryBug[] Bugs
);

public record BugzillaHistoryBug(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("history")] BugzillaHistoryEvent[] History
);

public record BugzillaHistoryEvent(
    [property: JsonPropertyName("when"), JsonConverter(typeof(JiraDateTimeConverter))]
        DateTime When,
    [property: JsonPropertyName("who")] string Who,
    [property: JsonPropertyName("changes")] BugzillaHistoryChange[] Changes
);

public record BugzillaHistoryChange(
    [property: JsonPropertyName("field_name")] string FieldName,
    [property: JsonPropertyName("removed")] string? Removed,
    [property: JsonPropertyName("added")] string? Added
);

public record BugzillaProductsResponse(
    [property: JsonPropertyName("products")] BugzillaProduct[] Products
);

public record BugzillaProductResponse(
    [property: JsonPropertyName("products")] BugzillaProduct[] Products
);

public record BugzillaProduct(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("is_active")] bool IsActive
);
