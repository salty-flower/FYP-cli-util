namespace DataCollection.Core.Models.IssueTracker.Responses;

/// <summary>
/// Rule-based, deterministic portion of the issue analysis.
/// Populated by deterministic code paths (e.g. inspection of labels, PR metadata, developer presence).
/// This record intentionally omits subjective rationale/confidence fields.
/// </summary>
public record DeterministicIssueAnalysis
{
    public required string[] DeveloperUsernames { get; set; }
    public required bool HasDeveloperJudgement { get; set; }

    // Deterministic fields (rule-based)
    public required bool? IsFixed { get; set; }

    // Additional deterministic explanation
    public required string NuanceOrExplanation { get; set; }
    public required string? AdditionalNotes { get; set; }
}

/// <summary>
/// Subjective portion of the analysis that requires human/LLM judgement.
/// Contains rationale and confidence values and is intended to be filled by the LLM.
/// </summary>
public record SubjectiveIssueAnalysis
{
    public required bool? IsRealBug { get; set; }
    public required string WhetherRealBugRationale { get; set; }
    public required double ConfidenceInWhetherRealBug { get; set; }

    public required bool? IsDuplicate { get; set; }
    public required string WhetherDuplicateRationale { get; set; }
    public required double ConfidenceInWhetherDuplicate { get; set; }
}

/// <summary>
/// Combined analysis response that wraps deterministic and subjective parts.
/// Deterministic fields should be computed first (by code). The Subjective block is then
/// populated by the LLM for the two subjective judgments (IsRealBug, IsDuplicate).
/// </summary>
public record IssueAnalysisResponse
{
    public required DeterministicIssueAnalysis Deterministic { get; set; }
    public required SubjectiveIssueAnalysis Subjective { get; set; }
}
