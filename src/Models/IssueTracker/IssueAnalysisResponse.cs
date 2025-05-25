namespace DataCollection.Models.IssueTracker;

public record IssueAnalysisResponse
{
    public required string[] DeveloperUsernames { get; set; }
    public required bool HasDeveloperJudgement { get; set; }
    public required bool? IsRealBug { get; set; }
    public required string WhetherRealBugRationale { get; set; }
    public required double ConfidenceInWhetherRealBug { get; set; }
    public required bool? IsDuplicate { get; set; }
    public required string WhetherDuplicateRationale { get; set; }
    public required double ConfidenceInWhetherDuplicate { get; set; }
    public required bool? IsFixed { get; set; }
    public required string WhetherFixedRationale { get; set; }
    public required double ConfidenceInWhetherFixed { get; set; }
    public required bool? IsFixedBeforeIssueRaised { get; set; }
    public required string WhetherFixedBeforeIssueRaisedRationale { get; set; }
    public required double ConfidenceInWhetherFixedBeforeIssueRaised { get; set; }

    public required bool? IsBugButWontFix { get; set; }
    public required bool? IsBugButWaitingForAction { get; set; }
    public required string WhetherWontFixRationale { get; set; }
    public required double ConfidenceInWhetherWontFix { get; set; }

    // public required double OverallConfidence { get; set; }
    public required string NuanceOrExplanation { get; set; }
    public required string? AdditionalNotes { get; set; }
}
