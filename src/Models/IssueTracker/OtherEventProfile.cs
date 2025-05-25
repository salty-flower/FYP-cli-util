using System;

namespace DataCollection.Models.IssueTracker;

public record OtherEventProfile
{
    public required string EventType { get; init; }
    public required string EventDescription
    {
        get;
        init => field = value.Trim().Replace("{}", string.Empty); // remove empty JSON placeholders or whitespace
    }
    public required DateTimeOffset OccurredAt { get; init; }
    public required UserProfile ActorProfile { get; init; }
}
