namespace DataCollection.Core.Models.IssueTracker;

public record UniversalUserProfile
{
    public required string Username { get; init; }
    public required BugTrackingProvider Provider { get; init; }
    public bool? IsCollaboratorOrMember { get; set; }
    public bool? IsContributor { get; set; }

    // Universal metrics (available in most systems)
    public int TotalIssuesOpened { get; init; }
    public int TotalIssuesAssigned { get; init; }
    public int TotalCommentsPosted { get; init; }

    // GitHub-specific metrics (nullable for other providers)
    public int? TotalPullRequests { get; init; }
    public int? TotalMergedPullRequests { get; init; }

    // Jira-specific metrics (nullable for other providers)
    public int? TotalStoriesCompleted { get; init; }
    public int? TotalBugsResolved { get; init; }

    // Platform-specific role detection (from analysis)
    public bool? IsMaintainer { get; set; }
    public bool? IsCommitter { get; set; }
    public bool? IsTriageOwner { get; set; }
    public string? RoleIndicators { get; set; } // Raw role text from platform

    // Computed properties
    public bool? IsDeveloper { get; set; }
    public double ActivityScore { get; set; } // Normalized activity across providers

    // Provider-specific data
    public ProviderSpecificData? ProviderSpecificData { get; init; }

    /// <summary>
    /// Provides a concise representation for LLM prompts, showing only relevant non-empty fields
    /// </summary>
    public string ToPromptString()
    {
        var parts = new List<string> { $"@{Username}" };

        // Add role indicators if available
        var roles = new List<string>();
        if (IsMaintainer == true)
            roles.Add("maintainer");
        if (IsCommitter == true)
            roles.Add("committer");
        if (IsTriageOwner == true)
            roles.Add("triage-owner");
        if (IsDeveloper == true && roles.Count == 0)
            roles.Add("developer");
        if (IsCollaboratorOrMember == true)
            roles.Add("collaborator");
        if (IsContributor == true)
            roles.Add("contributor");

        if (roles.Count > 0)
            parts.Add($"({string.Join(", ", roles)})");

        // Add activity metrics if meaningful
        var metrics = new List<string>();
        if (TotalIssuesOpened > 0)
            metrics.Add($"{TotalIssuesOpened} issues");
        if (TotalIssuesAssigned > 0)
            metrics.Add($"{TotalIssuesAssigned} assigned");
        if (TotalCommentsPosted > 0)
            metrics.Add($"{TotalCommentsPosted} comments");
        if (TotalPullRequests > 0)
            metrics.Add($"{TotalPullRequests} PRs");
        if (TotalMergedPullRequests > 0)
            metrics.Add($"{TotalMergedPullRequests} merged");

        if (metrics.Count > 0)
            parts.Add($"[{string.Join(", ", metrics)}]");

        // Add raw role indicators if available and different from parsed roles
        if (!string.IsNullOrEmpty(RoleIndicators) && roles.Count == 0)
            parts.Add($"({RoleIndicators})");

        return string.Join(" ", parts);
    }
}

public record UniversalIssueProfile
{
    public required Issue CoreIssue { get; init; }
    public required Repository CoreRepository { get; init; }
    public required UniversalUserProfile AuthorProfile { get; init; }

    // Universal data
    public required List<Comment> Comments { get; init; }
    public required List<IssueEvent> Events { get; init; }
    public required List<UniversalUserProfile> Participants { get; init; }

    // Computed metrics
    public TimeSpan? TimeToFirstResponse { get; init; }
    public TimeSpan? TimeToResolution { get; init; }
    public int ParticipantCount => Participants.Count;
    public int CommentCount => Comments.Count;

    // Provider-specific raw data (for advanced analysis)
    public ProviderSpecificData? ProviderSpecificData { get; init; }
}

public enum LabelEventType
{
    Added,
    Removed,
}

public enum AssignmentEventType
{
    Assigned,
    Unassigned,
}

// Event-specific profiles for detailed analysis
public record CommentEvent
{
    public required Comment Comment { get; init; }
    public required UniversalUserProfile By { get; init; }
}

public record LabelEvent
{
    public required DateTimeOffset OccurredAt { get; init; }
    public required string LabelName { get; init; }
    public required string LabelColor { get; init; }
    public required UniversalUserProfile By { get; init; }
    public required LabelEventType EventType { get; init; }
}

public record AssignmentEvent
{
    public required DateTimeOffset OccurredAt { get; init; }
    public required string AssigneeName { get; init; }
    public required UniversalUserProfile By { get; init; }
    public required AssignmentEventType EventType { get; init; }
}
