using System.ComponentModel;
using System.Text.Json.Serialization;

namespace DataCollection.Models.IssueTracker;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IssueStatus
{
    [Description("Issue is Pending.")]
    Pending,

    [Description("Issue is Confirmed and Fixed.")]
    ConfirmedFixed,

    [Description("Issue is Confirmed and Waiting for Action.")]
    ConfirmedWaitingForAction,

    [Description("Issue is Confirmed but Not Fixed.")]
    ConfirmedWontFix,

    [Description("Issue is a Duplicate.")]
    Duplicate,

    [Description("Issue was Fixed Before Report.")]
    FixedBeforeReport,

    [Description("Issue is Not a Bug.")]
    NotABug,
}
