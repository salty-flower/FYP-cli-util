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
    public string? CommitId { get; init; }
    public string? CommitUrl { get; init; }
}

public record CommitAuthorBriefing
{
    public string? Login { get; init; }
    public string? Name { get; init; }
    public string? Email { get; init; }
    public DateTimeOffset? Date { get; init; }
    public string? HtmlUrl { get; init; }
}

public record CommitDiffStatBriefing
{
    public int? Additions { get; init; }
    public int? Deletions { get; init; }
    public int? TotalChanges { get; init; }
}

public record CommitFileBriefing
{
    public required string FileName { get; init; }
    public string? Status { get; init; }
    public int? Additions { get; init; }
    public int? Deletions { get; init; }
    public int? Changes { get; init; }
}

public record CommitBriefing
{
    public required string Sha { get; init; }
    public string? HtmlUrl { get; init; }
    public string? MessageHeadline { get; init; }
    public string? MessageBody { get; init; }
    public CommitAuthorBriefing? Author { get; init; }
    public CommitDiffStatBriefing? Stats { get; init; }
    public required CommitFileBriefing[] Files { get; init; }
    public DateTimeOffset? AuthoredDate => Author?.Date;
}

public record PullRequestFileBriefing
{
    public required string FileName { get; init; }
    public string? Status { get; init; }
    public int? Additions { get; init; }
    public int? Deletions { get; init; }
    public int? Changes { get; init; }
}

public record PullRequestBriefing
{
    public required int Number { get; init; }
    public string? Title { get; init; }
    public string? Body { get; init; }
    public string? State { get; init; }
    public string? HtmlUrl { get; init; }
    public string? AuthorLogin { get; init; }
    public string? AuthorName { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? MergedAt { get; init; }
    public DateTimeOffset? ClosedAt { get; init; }
    public int? Additions { get; init; }
    public int? Deletions { get; init; }
    public int? ChangedFiles { get; init; }
    public required PullRequestFileBriefing[] Files { get; init; }
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
    public required CommitBriefing[] AssociatedCommitBriefings { get; init; }
    public PullRequestBriefing? AssociatedPullRequest { get; init; }
}
