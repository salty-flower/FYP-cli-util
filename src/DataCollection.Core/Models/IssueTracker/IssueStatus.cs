using System.ComponentModel;
using System.Text.Json.Serialization;

namespace DataCollection.Core.Models.IssueTracker;

/// <summary>
/// Canonical issue status values aligned with the decision-tree used for analysis.
/// This enum contains only the statuses that appear as leaves in the decision tree:
/// Inconclusive, NotABug, Fixed, Duplicate, Confirmed.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<IssueStatus>))]
public enum IssueStatus
{
    [Description("Developer comments are inconclusive for classification.")]
    Inconclusive,

    [Description("The issue is explicitly not a bug (developer indicates not a bug).")]
    NotABug,

    [Description("The issue has been fixed (evidence: associated merged PR).")]
    Fixed,

    [Description("The issue is a duplicate of another issue/report.")]
    Duplicate,

    [Description(
        "The issue is confirmed (developer acknowledges the bug or there is sufficient evidence)."
    )]
    Confirmed,
}
