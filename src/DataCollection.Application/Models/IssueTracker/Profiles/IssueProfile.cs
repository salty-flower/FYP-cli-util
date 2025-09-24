using DataCollection.Core.Models.IssueTracker;
using DataCollection.Infrastructure.Models.GitHub;
using GitHub.Models;

namespace DataCollection.Application.Models.IssueTracker.Profiles;

public record CommentEventProfile
{
    public required IssueComment SdkComment { get; init; }
    public required UserProfile By { get; init; }
}

public record LabelEventProfile
{
    public required DateTimeOffset OccuredAt { get; init; }
    public required Label SdkLabel { get; init; }
    public required UserProfile By { get; init; }
    public required LabelEventType Event { get; init; }
}

public record OtherEventProfile
{
    public required string EventType { get; init; }
    public required string EventDescription
    {
        get;
        init => field = value.Trim().Replace("{}", string.Empty); // remove empty JSON placeholders or whitespace
    }
    public required DateTimeOffset OccurredAt { get; init; }
    public required UserProfile By { get; init; }
}

public record IssueProfile
{
    public required GitHubIssue SdkIssue { get; init; }
    public required FullRepository SdkRepository { get; init; }
    public required UserProfile AuthorProfile { get; init; }
    public required string RepositoryFullName { get; init; }
    public bool IsClosed { get; init; }
    public bool HasAssociatedPullRequest { get; init; }
    public required LabelEventProfile[] LabelEvents { get; init; }
    public required CommentEventProfile[] CommentEvents { get; init; }
    public required OtherEventProfile[] OtherEvents { get; init; }
}
