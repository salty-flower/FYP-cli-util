namespace DataCollection.Core.Models.IssueTracker.Responses;

/// <summary>
/// Complete issue analysis result including metadata and analysis response.
/// Used for batch export to JSONL format.
/// </summary>
public record IssueAnalysisResult
{
    public required string Owner { get; init; }
    public required string Repo { get; init; }
    public required long IssueNumber { get; init; }
    public required string Url { get; init; }
    public required IssueAnalysisResponse Analysis { get; init; }
}
