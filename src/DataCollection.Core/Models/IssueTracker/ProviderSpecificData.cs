namespace DataCollection.Core.Models.IssueTracker;

public abstract record ProviderSpecificData
{
    public abstract BugTrackingProvider Provider { get; }
}

// GitHub-specific data
public sealed record GitHubRepositoryData : ProviderSpecificData
{
    public override BugTrackingProvider Provider => BugTrackingProvider.GitHub;
    public required long Id { get; init; }
    public required string DefaultBranch { get; init; }
    public required string Language { get; init; }
    public required int Stars { get; init; }
    public required int Forks { get; init; }
    public required bool IsPrivate { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record GitHubIssueData : ProviderSpecificData
{
    public override BugTrackingProvider Provider => BugTrackingProvider.GitHub;
    public required long Number { get; init; }
    public required string State { get; init; }
    public required bool Locked { get; init; }
    public required int Comments { get; init; }
    public required string Milestone { get; init; }
    public required bool IsPullRequest { get; init; }
}

public sealed record GitHubCommentData : ProviderSpecificData
{
    public override BugTrackingProvider Provider => BugTrackingProvider.GitHub;
    public required string HtmlUrl { get; init; }
    public required string AuthorAssociation { get; init; }
}

public sealed record GitHubUserData : ProviderSpecificData
{
    public override BugTrackingProvider Provider => BugTrackingProvider.GitHub;
    public required string Name { get; init; }
    public required string Email { get; init; }
    public required string Company { get; init; }
    public required string Location { get; init; }
    public required int PublicRepos { get; init; }
    public required int Followers { get; init; }
    public required int Following { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record GitHubEventData : ProviderSpecificData
{
    public override BugTrackingProvider Provider => BugTrackingProvider.GitHub;
    public required string CommitId { get; init; }
    public required string Label { get; init; }
    public required string Assignee { get; init; }
    public required string Assigner { get; init; }
}

// Jira-specific data (for future implementation)
public sealed record JiraRepositoryData : ProviderSpecificData
{
    public override BugTrackingProvider Provider => BugTrackingProvider.Jira;
    public required string ProjectKey { get; init; }
    public required string ProjectType { get; init; }
    public required string Lead { get; init; }
    public required List<string> Components { get; init; }
    public required List<string> Versions { get; init; }
}

public sealed record JiraIssueData : ProviderSpecificData
{
    public override BugTrackingProvider Provider => BugTrackingProvider.Jira;
    public required string Key { get; init; }
    public required string IssueType { get; init; }
    public required string Resolution { get; init; }
    public required string Reporter { get; init; }
    public required List<string> Components { get; init; }
    public required List<string> FixVersions { get; init; }
    public required string Environment { get; init; }
    public required int? StoryPoints { get; init; }
}

public sealed record JiraUserData : ProviderSpecificData
{
    public override BugTrackingProvider Provider => BugTrackingProvider.Jira;
    public required string AccountId { get; init; }
    public required string DisplayName { get; init; }
    public required string EmailAddress { get; init; }
    public required List<string> Groups { get; init; }
    public required string TimeZone { get; init; }
}

// Bugzilla-specific data (for future implementation)
public sealed record BugzillaRepositoryData : ProviderSpecificData
{
    public override BugTrackingProvider Provider => BugTrackingProvider.Bugzilla;
    public required string Product { get; init; }
    public required string Description { get; init; }
    public required List<string> Components { get; init; }
    public required List<string> Versions { get; init; }
    public required List<string> Milestones { get; init; }
}

public sealed record BugzillaIssueData : ProviderSpecificData
{
    public override BugTrackingProvider Provider => BugTrackingProvider.Bugzilla;
    public required int BugId { get; init; }
    public required string Product { get; init; }
    public required string Component { get; init; }
    public required string Version { get; init; }
    public required string TargetMilestone { get; init; }
    public required string OperatingSystem { get; init; }
    public required string Platform { get; init; }
    public required string Severity { get; init; }
    public required string Classification { get; init; }
}

public sealed record BugzillaUserData : ProviderSpecificData
{
    public override BugTrackingProvider Provider => BugTrackingProvider.Bugzilla;
    public required string RealName { get; init; }
    public required string Email { get; init; }
    public required List<string> Groups { get; init; }
    public required bool CanConfirm { get; init; }
    public required bool CanEditBugs { get; init; }
}

// GitLab-specific data (for future implementation)
public sealed record GitLabRepositoryData : ProviderSpecificData
{
    public override BugTrackingProvider Provider => BugTrackingProvider.GitLab;
    public required long Id { get; init; }
    public required string Namespace { get; init; }
    public required string Path { get; init; }
    public required string Visibility { get; init; }
    public required int Stars { get; init; }
    public required int Forks { get; init; }
    public required List<string> Topics { get; init; }
}

public sealed record GitLabIssueData : ProviderSpecificData
{
    public override BugTrackingProvider Provider => BugTrackingProvider.GitLab;
    public required long Iid { get; init; }
    public required string State { get; init; }
    public required string IssueType { get; init; }
    public required int? Weight { get; init; }
    public required List<string> Labels { get; init; }
    public required string Milestone { get; init; }
    public required DateTimeOffset? DueDate { get; init; }
}

public sealed record GitLabUserData : ProviderSpecificData
{
    public override BugTrackingProvider Provider => BugTrackingProvider.GitLab;
    public required long Id { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
    public required string State { get; init; }
    public required string WebUrl { get; init; }
    public required bool IsAdmin { get; init; }
}

// Unknown provider fallback
public sealed record UnknownProviderData : ProviderSpecificData
{
    public override BugTrackingProvider Provider => BugTrackingProvider.Unknown;
    public required string RawData { get; init; }
    public required string DataType { get; init; }
}
