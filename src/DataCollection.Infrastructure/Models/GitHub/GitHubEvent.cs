using System.Text.Json.Serialization;

namespace DataCollection.Infrastructure.Models.GitHub;

public class GitHubEvent
{
    public long Id { get; set; }

    public string? NodeId { get; set; }
    public string? Url { get; set; }
    public GitHubUser? Actor { get; set; }
    public string? Event { get; set; }

    public string? CommitId { get; set; }

    public string? CommitUrl { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public GitHubLabel? Label { get; set; }
    public GitHubUser? Assignee { get; set; }
    public GitHubUser? Assigner { get; set; }
    public GitHubUser? ReviewRequester { get; set; }
    public GitHubUser? RequestedReviewer { get; set; }
    public GitHubMilestone? Milestone { get; set; }
    public GitHubRename? Rename { get; set; }
    public GitHubDismissedReview? DismissedReview { get; set; }

    public GitHubEventDetails ExtractDetails() =>
        new()
        {
            Rename = Rename,
            DismissedReview = DismissedReview,
            MilestoneTitle = Milestone?.Title,
            Assignee = Assignee?.Login,
            Assigner = Assigner?.Login,
            ReviewRequester = ReviewRequester?.Login,
        };
}

public record GitHubEventDetails
{
    public GitHubRename? Rename { get; init; }
    public GitHubDismissedReview? DismissedReview { get; init; }
    public string? MilestoneTitle { get; init; }
    public string? Assignee { get; init; }
    public string? Assigner { get; init; }
    public string? ReviewRequester { get; init; }
    public string? RequestedReviewer { get; init; }
}

public class GitHubUser
{
    public string? Login { get; set; }
    public long Id { get; set; }
    public string? NodeId { get; set; }
    public string? AvatarUrl { get; set; }
    public string? GravatarId { get; set; }
    public string? Url { get; set; }
    public string? HtmlUrl { get; set; }
    public string? Type { get; set; }
    public bool SiteAdmin { get; set; }
}

public class GitHubLabel
{
    public string? Name { get; set; }
    public string? Color { get; set; }
}

public class GitHubMilestone
{
    public string? Title { get; set; }
}

public class GitHubRename
{
    public string? From { get; set; }
    public string? To { get; set; }
}

public class GitHubDismissedReview
{
    public string? State { get; set; }
    public long ReviewId { get; set; }
    public string? DismissalMessage { get; set; }
    public string? DismissalCommitId { get; set; }
}
