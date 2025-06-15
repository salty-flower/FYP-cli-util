namespace DataCollection.Application.Models.Commands;

public sealed record DecideIssueStatusCommand(
    string? Url = null,
    string? Owner = null,
    string? RepoName = null,
    long? IssueNumber = null,
    bool SaveResults = false,
    bool UseCache = true
);

public sealed record ProcessIssueBatchCommand(
    string InputFile,
    bool SaveResults = true,
    bool UseCache = true,
    bool UseBatchApi = true,
    string? BatchJobId = null
);

public sealed record IssueInfo(string Owner, string Repo, long IssueNumber)
{
    public string Url => $"https://github.com/{Owner}/{Repo}/issues/{IssueNumber}";
};
