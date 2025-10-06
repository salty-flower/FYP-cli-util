namespace DataCollection.Infrastructure.Models.GitHub;

public class GitHubPullRequestDetails
{
    public long Id { get; set; }
    public int Number { get; set; }
    public string? State { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
    public GitHubUser? User { get; set; }
    public GitHubUser? MergedBy { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset? MergedAt { get; set; }
    public int? Additions { get; set; }
    public int? Deletions { get; set; }
    public int? ChangedFiles { get; set; }
    public string? HtmlUrl { get; set; }
}

public class GitHubPullRequestFile
{
    public string? Sha { get; set; }
    public string? Filename { get; set; }
    public string? Status { get; set; }
    public int? Additions { get; set; }
    public int? Deletions { get; set; }
    public int? Changes { get; set; }
    public string? BlobUrl { get; set; }
    public string? RawUrl { get; set; }
    public string? ContentsUrl { get; set; }
    public string? Patch { get; set; }
}
