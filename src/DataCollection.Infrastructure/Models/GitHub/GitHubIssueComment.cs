using System.Text.Json.Serialization;

namespace DataCollection.Infrastructure.Models.GitHub;

public class GitHubIssueComment
{
    public string? Body { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    public GitHubUser? User { get; set; }

    public string? HtmlUrl { get; set; }
}
