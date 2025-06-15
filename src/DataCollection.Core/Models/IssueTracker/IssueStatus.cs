using System.ComponentModel;
using System.Text.Json.Serialization;

namespace DataCollection.Core.Models.IssueTracker;

[JsonConverter(typeof(JsonStringEnumConverter<IssueStatus>))]
public enum IssueStatus
{
    [Description("Issue is open and needs attention.")]
    Open,

    [Description("Issue is closed or completed.")]
    Closed,

    [Description("Issue is currently being worked on.")]
    InProgress,

    [Description("Issue has been resolved or fixed.")]
    Resolved,

    [Description("Issue will not be fixed.")]
    Wontfix,

    [Description("Issue is a duplicate of another issue.")]
    Duplicate,

    [Description("Issue is invalid or not a real issue.")]
    Invalid,

    [Description("Issue status is unknown or unmapped.")]
    Unknown,

    // Legacy statuses for backward compatibility
    [Description("Issue is pending review or analysis.")]
    Pending,

    [Description("Issue is confirmed and has been fixed.")]
    ConfirmedFixed,

    [Description("Issue is confirmed and waiting for action.")]
    ConfirmedWaitingForAction,

    [Description("Issue is confirmed but will not be fixed.")]
    ConfirmedWontFix,

    [Description("Issue was already fixed before being reported.")]
    FixedBeforeReport,

    [Description("Issue is not actually a bug.")]
    NotABug,
}
