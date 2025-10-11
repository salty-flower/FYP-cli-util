namespace DataCollection.Infrastructure.Models.GitHub;

public class ClosingPullRequest
{
    public required int Number { get; init; }
    public string? Title { get; init; }
    public string? Url { get; init; }
    public bool? Merged { get; init; }
    public DateTimeOffset? MergedAt { get; init; }
}
