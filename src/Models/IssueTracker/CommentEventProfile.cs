using Octokit;

namespace DataCollection.Models.IssueTracker;

public record CommentEventProfile
{
    public required IssueComment Comment { get; init; }
    public required UserProfile By { get; init; }
}