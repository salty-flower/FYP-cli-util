using System.Text.Json.Serialization;

namespace DataCollection.Infrastructure.Models.Bugzilla;

public record BugzillaBugResponse
{
    [JsonPropertyName("bugs")]
    public List<BugzillaBug> Bugs { get; init; } = [];
}

public record BugzillaBug
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("resolution")]
    public string? Resolution { get; init; }

    [JsonPropertyName("assigned_to")]
    public string AssignedTo { get; init; } = string.Empty;

    [JsonPropertyName("assigned_to_detail")]
    public BugzillaUser? AssignedToDetail { get; init; }

    [JsonPropertyName("creator")]
    public string Creator { get; init; } = string.Empty;

    [JsonPropertyName("creator_detail")]
    public BugzillaUser? CreatorDetail { get; init; }

    [JsonPropertyName("creation_time")]
    public DateTimeOffset CreationTime { get; init; }

    [JsonPropertyName("last_change_time")]
    public DateTimeOffset LastChangeTime { get; init; }

    [JsonPropertyName("cc")]
    public List<string> CC { get; init; } = [];

    [JsonPropertyName("cc_detail")]
    public List<BugzillaUser> CCDetail { get; init; } = [];

    [JsonPropertyName("component")]
    public string Component { get; init; } = string.Empty;

    [JsonPropertyName("product")]
    public string Product { get; init; } = string.Empty;

    [JsonPropertyName("priority")]
    public string Priority { get; init; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; init; } = string.Empty;

    [JsonPropertyName("keywords")]
    public List<string> Keywords { get; init; } = [];

    [JsonPropertyName("classification")]
    public string Classification { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("platform")]
    public string Platform { get; init; } = string.Empty;

    [JsonPropertyName("op_sys")]
    public string OperatingSystem { get; init; } = string.Empty;
}

public record BugzillaUser
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("email")]
    public string Email { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("real_name")]
    public string RealName { get; init; } = string.Empty;

    [JsonPropertyName("nick")]
    public string? Nick { get; init; }
}

public record BugzillaCommentsResponse
{
    [JsonPropertyName("bugs")]
    public Dictionary<string, BugzillaBugComments> Bugs { get; init; } = new();
}

public record BugzillaBugComments
{
    [JsonPropertyName("comments")]
    public List<BugzillaComment> Comments { get; init; } = [];
}

public record BugzillaComment
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("bug_id")]
    public long BugId { get; init; }

    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("creator")]
    public string Creator { get; init; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; init; } = string.Empty;

    [JsonPropertyName("time")]
    public DateTimeOffset Time { get; init; }

    [JsonPropertyName("creation_time")]
    public DateTimeOffset CreationTime { get; init; }

    [JsonPropertyName("is_private")]
    public bool IsPrivate { get; init; }

    [JsonPropertyName("is_markdown")]
    public bool IsMarkdown { get; init; }
}

public record BugzillaHistoryResponse
{
    [JsonPropertyName("bugs")]
    public List<BugzillaBugHistory> Bugs { get; init; } = [];
}

public record BugzillaBugHistory
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("history")]
    public List<BugzillaHistoryEvent> History { get; init; } = [];
}

public record BugzillaHistoryEvent
{
    [JsonPropertyName("when")]
    public DateTimeOffset When { get; init; }

    [JsonPropertyName("who")]
    public string Who { get; init; } = string.Empty;

    [JsonPropertyName("changes")]
    public List<BugzillaChange> Changes { get; init; } = [];
}

public record BugzillaChange
{
    [JsonPropertyName("field_name")]
    public string FieldName { get; init; } = string.Empty;

    [JsonPropertyName("removed")]
    public string? Removed { get; init; }

    [JsonPropertyName("added")]
    public string? Added { get; init; }
}

public record BugzillaProductsResponse
{
    [JsonPropertyName("products")]
    public List<BugzillaProduct> Products { get; init; } = [];
}

public record BugzillaProduct
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("is_active")]
    public bool IsActive { get; init; }

    [JsonPropertyName("classification")]
    public string Classification { get; init; } = string.Empty;
}

public record BugzillaSearchResponse
{
    [JsonPropertyName("bugs")]
    public List<BugzillaBug> Bugs { get; init; } = [];

    [JsonPropertyName("faults")]
    public List<object> Faults { get; init; } = [];
}
