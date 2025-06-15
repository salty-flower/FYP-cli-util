namespace DataCollection.Application.Features.SemanticAgents.Models;

public record DiscoveryTask
{
    public required string Id { get; init; }
    public required string Description { get; init; }
    public required DiscoveryTaskType Type { get; init; }
    public required string Source { get; init; } // URL, PDF path, text content
    public List<string> Keywords { get; init; } = [];
    public Dictionary<string, string> Metadata { get; init; } = [];
    public DiscoveryTaskStatus Status { get; init; } = DiscoveryTaskStatus.Pending;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<DiscoveryResult> Results { get; init; } = [];
}

public enum DiscoveryTaskType
{
    BugListDiscovery,
    ArtifactRepositoryDiscovery,
    VulnerabilityDiscovery,
    DocumentationDiscovery,
}

public enum DiscoveryTaskStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    PartialSuccess,
}

public record DiscoveryResult
{
    public required string Type { get; init; } // "bug_list", "repository", "artifact", "vulnerability"
    public required string Url { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required double Confidence { get; init; }
    public List<string> ExtractedKeywords { get; init; } = [];
    public Dictionary<string, string> Metadata { get; init; } = [];
    public DiscoveryContext Context { get; init; } = new();
}

public record DiscoveryContext
{
    public string? SourceLocation { get; init; } // Where it was found
    public List<string> SurroundingText { get; init; } = [];
    public string? SectionTitle { get; init; }
    public int? PageNumber { get; init; }
    public List<string> RelatedLinks { get; init; } = [];
}

public record AgentPlan
{
    public required string TaskId { get; init; }
    public required List<AgentAction> Actions { get; init; }
    public required string Reasoning { get; init; }
    public required List<string> ExpectedOutcomes { get; init; } = [];
    public required Dictionary<string, string> Parameters { get; init; } = [];
}

public record AgentAction
{
    public required string Id { get; init; }
    public required string ActionType { get; init; } // "search_text", "extract_urls", "analyze_content", "validate_links"
    public required string Tool { get; init; }
    public required Dictionary<string, object> Parameters { get; init; } = [];
    public required List<string> Dependencies { get; init; } = []; // IDs of actions this depends on
    public required AgentActionStatus Status { get; init; } = AgentActionStatus.Pending;
    public required string? Result { get; init; }
    public required string? Error { get; init; }
}

public enum AgentActionStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Skipped,
}

public record AgentReflection
{
    public required string TaskId { get; init; }
    public required string Summary { get; init; }
    public required List<string> SuccessfulActions { get; init; }
    public required List<string> FailedActions { get; init; }
    public required List<string> LessonsLearned { get; init; }
    public required List<string> ImprovementSuggestions { get; init; }
    public required double OverallConfidence { get; init; }
    public List<string> NextSteps { get; init; } = [];
}
