using System.Text.Json.Serialization;

namespace DataCollection.Models.IssueTracker;

public record IssueAnalysisResponse
{
    public required bool IsRealBug { get; set; }
    public required bool IsDuplicate { get; set; }
    public required bool IsFixed { get; set; }
    public required bool IsFixedBeforeIssueRaised { get; set; }
    public required bool WontFix { get; set; }
    public required double Confidence { get; set; }
    public required string Explanation { get; set; }

}
