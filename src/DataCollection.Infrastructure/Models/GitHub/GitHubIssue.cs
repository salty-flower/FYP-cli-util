using System.Text.Json.Serialization;

namespace DataCollection.Infrastructure.Models.GitHub;

public class GitHubIssue
{
    public long Id { get; set; }
    public int Number { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
    public string? State { get; set; }

    [JsonPropertyName("state_reason")]
    public string? StateReasonString { get; set; }

    // Property to match SDK interface - maps string to enum-like behavior
    [JsonIgnore]
    public object? StateReason =>
        StateReasonString != null ? new StateReasonWrapper(StateReasonString) : null;
    public bool Locked { get; set; }

    public string? ActiveLockReason { get; set; }
    public GitHubUser? User { get; set; }
    public GitHubLabel[]? Labels { get; set; }

    [JsonPropertyName("created_at")]
    // This JSON property name annotationis COMPULSORY - do not remove.
    // Without that DateTimeOffset gets default value.
    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public GitHubPullRequest? PullRequest { get; set; }

    public string? HtmlUrl { get; set; }
}

public class GitHubPullRequest
{
    public string? HtmlUrl { get; set; }
    public DateTimeOffset? MergedAt { get; set; }
}

public class StateReasonWrapper(string value)
{
    public StateReasonWrapper Value => this;

    public string GetName() => value;
}
