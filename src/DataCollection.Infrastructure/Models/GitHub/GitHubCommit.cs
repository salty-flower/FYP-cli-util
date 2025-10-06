namespace DataCollection.Infrastructure.Models.GitHub;

public class GitHubCommit
{
    public string? Sha { get; set; }
    public string? HtmlUrl { get; set; }
    public GitHubCommitInner? Commit { get; set; }
    public GitHubUser? Author { get; set; }
    public GitHubUser? Committer { get; set; }
    public GitHubCommitStats? Stats { get; set; }
    public GitHubCommitFile[]? Files { get; set; }
}

public class GitHubCommitInner
{
    public GitHubCommitPerson? Author { get; set; }
    public GitHubCommitPerson? Committer { get; set; }
    public string? Message { get; set; }
}

public class GitHubCommitPerson
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public DateTimeOffset? Date { get; set; }
}

public class GitHubCommitStats
{
    public int? Total { get; set; }
    public int? Additions { get; set; }
    public int? Deletions { get; set; }
}

public class GitHubCommitFile
{
    public string? Sha { get; set; }
    public string? Filename { get; set; }
    public string? Status { get; set; }
    public int? Additions { get; set; }
    public int? Deletions { get; set; }
    public int? Changes { get; set; }
    public string? BlobUrl { get; set; }
    public string? RawUrl { get; set; }
    public string? Patch { get; set; }
}
