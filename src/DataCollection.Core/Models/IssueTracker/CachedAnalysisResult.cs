using DataCollection.Core.Models.IssueTracker.Responses;

namespace DataCollection.Core.Models.IssueTracker;

public class CachedAnalysisResult
{
    public required string Owner { get; set; }
    public required string Repository { get; set; }
    public required long IssueNumber { get; set; }
    public required IssueStatus Status { get; set; }
    public required IssueAnalysisResponse Analysis { get; set; }
}
