namespace DataCollection.Core.Models.IssueTracker;

public enum BugTrackingProvider
{
    GitHub,
    Jira,
    Bugzilla,
    GitLab,
    GnuSavannah,
    Trac,
    Unknown,
}

public record Repository
{
    public required string Identifier { get; init; } // GitHub: "owner/repo", Jira: "PROJECT", Bugzilla: "product"
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Url { get; init; }
    public BugTrackingProvider Provider { get; init; }
    public ProviderSpecificData? ProviderSpecificData { get; init; }
}

public record Issue
{
    public required string Id { get; init; } // GitHub: "#123", Jira: "PROJ-123", Bugzilla: "123456"
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required IssueStatus Status { get; init; }
    public required string Author { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public DateTimeOffset? ClosedAt { get; init; }
    public List<string> Labels { get; init; } = [];
    public List<string> Assignees { get; init; } = [];
    public string? Priority { get; init; }
    public string? Severity { get; init; }
    public BugTrackingProvider Provider { get; init; }
    public ProviderSpecificData? ProviderSpecificData { get; init; }
}

public record Comment
{
    public required string Id { get; init; }
    public required string Author { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public BugTrackingProvider Provider { get; init; }
    public ProviderSpecificData? ProviderSpecificData { get; init; }
}

public record IssueEvent
{
    public required string Id { get; init; }
    public required string EventType { get; init; } // "labeled", "assigned", "commented", etc.
    public required string Actor { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string? Description { get; init; }
    public BugTrackingProvider Provider { get; init; }
    public ProviderSpecificData? ProviderSpecificData { get; init; }
}
