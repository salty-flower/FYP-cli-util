using System.Text.Json.Serialization;

namespace DataCollection.Infrastructure.Models.GitHub;

public class GitHubTimelineEvent
{
    public string? Event { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public GitHubUser? Actor { get; set; }
    public string? CommitId { get; set; }
    public string? CommitUrl { get; set; }
    public GitHubTimelineSource? Source { get; set; }

    public GitHubTimelineEventDetails ExtractDetails() =>
        new()
        {
            SourceType = Source?.Type,
            SourceIssueNumber = Source?.Issue?.Number,
            SourceIssueHtmlUrl = Source?.Issue?.HtmlUrl,
            PullRequestHtmlUrl = Source?.Issue?.PullRequest?.HtmlUrl,
            PullRequestMergedAt = Source?.Issue?.PullRequest?.MergedAt,
        };
}

public record GitHubTimelineEventDetails
{
    public string? SourceType { get; init; }
    public int? SourceIssueNumber { get; init; }
    public string? SourceIssueHtmlUrl { get; init; }
    public string? PullRequestHtmlUrl { get; init; }
    public DateTimeOffset? PullRequestMergedAt { get; init; }
}

public class GitHubTimelineSource
{
    public string? Type { get; set; }
    public GitHubTimelineSourceIssue? Issue { get; set; }
}

public class GitHubTimelineSourceIssue
{
    public int? Number { get; set; }
    public string? HtmlUrl { get; set; }
    public GitHubTimelinePullRequest? PullRequest { get; set; }
}

public class GitHubTimelinePullRequest
{
    public string? HtmlUrl { get; set; }
    public DateTimeOffset? MergedAt { get; set; }
}
