namespace DataCollection.Models.IssueTracker;

public record UserProfile
{
    public required string Login { get; init; }
    public required bool IsContributor { get; init; }

    public required int TotalIssues { get; init; }
    public required int TotalPullRequests { get; init; }
    public required int TotalMergedPullRequests { get; init; }

    public bool? IsDeveloper { get; set; }
}