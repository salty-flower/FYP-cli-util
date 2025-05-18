using System;
using Octokit;

namespace DataCollection.Models.IssueTracker;

public record LabelEventProfile
{
    public required DateTimeOffset OccuredAt { get; init; }
    public required Label Label { get; init; }
    public required UserProfile By { get; init; }
    public required LabelEvent Event { get; init; }
}