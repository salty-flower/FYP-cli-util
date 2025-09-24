namespace DataCollection.Core.Models.IssueTracker.Responses;

public record IssueAnalysisResponse
{
    public required string[] DeveloperUsernames { get; set; }
    public required bool HasDeveloperJudgement { get; set; }

    // Subjective fields (decided by LLM)
    public required bool? IsRealBug { get; set; }
    public required string WhetherRealBugRationale { get; set; }
    public required double ConfidenceInWhetherRealBug { get; set; }

    public required bool? IsDuplicate { get; set; }
    public required string WhetherDuplicateRationale { get; set; }
    public required double ConfidenceInWhetherDuplicate { get; set; }

    // Deterministic fields (rule-based; rationale/confidence removed)
    public required bool? IsFixed { get; set; }
    public required bool? IsFixedBeforeIssueRaised { get; set; }

    public required bool? IsBugButWontFix { get; set; }
    public required bool? IsBugButWaitingForAction { get; set; }

    // public required double OverallConfidence { get; set; }
    public required string NuanceOrExplanation { get; set; }
    public required string? AdditionalNotes { get; set; }
}
