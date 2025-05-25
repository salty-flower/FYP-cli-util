using Octokit;

namespace DataCollection.Models.IssueTracker;

public record IssueProfile
{
    public required Issue OctokitIssue { get; init; }
    public required Repository OctokitRepository { get; init; }
    public required UserProfile AuthorProfile { get; init; }
    public required string RepositoryFullName { get; init; }
    public bool IsClosed { get; init; }
    public bool HasAssociatedPullRequest { get; init; }
    public required LabelEventProfile[] LabelEvents { get; init; }
    public required CommentEventProfile[] CommentEvents { get; init; }
    public required OtherEventProfile[] OtherEvents { get; init; }
}
