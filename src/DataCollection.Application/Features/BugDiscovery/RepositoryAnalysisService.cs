using System.Text.RegularExpressions;
using DataCollection.Application.Common.Services;
using DataCollection.Common.Extensions;
using DataCollection.Core.Models;
using DataCollection.Infrastructure.Clients.IssueTrackers;
using DataCollection.Infrastructure.Models.BugList;
using DataCollection.Infrastructure.Models.GitHub;
using Microsoft.Extensions.Logging;

namespace DataCollection.Application.Features.BugDiscovery;

public partial class RepositoryAnalysisService(
    IGitHubClient gitHubClient,
    ILogger<RepositoryAnalysisService> logger,
    UrlProcessingService urlProcessingService,
    ValidationService validationService
)
{
    private const int MaxRepositoryRetries = 3;

    public async Task<RepositoryAnalysisResult> ExploreRepositoryBugLists(
        IReadOnlyList<ArtifactRepository> repositories,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        validationService.ValidatePaper(paper);
        logger.LogOperationStart("Repository exploration", repositories.Count);

        var analysisTasks = repositories.Select(repo =>
            AnalyzeSingleRepository(repo, paper, cancellationToken)
        );
        var results = await Task.WhenAll(analysisTasks);

        var bugLists = results.SelectMany(r => r.BugLists).ToList();
        var updatedRepositories = results
            .Select(r => r.UpdatedRepository)
            .Where(r => r != null)
            .Select(r => r!)
            .ToList();

        logger.LogInformation(
            "Bug list discovery via {Method}: {BugListCount} bug lists, {RepositoryCount} repositories",
            "Repository exploration",
            bugLists.Count,
            updatedRepositories.Count
        );

        return new RepositoryAnalysisResult
        {
            BugLists = bugLists.DistinctBy(b => b.Url).ToList(),
            ArtifactRepositories = updatedRepositories.DistinctBy(r => r.Url).ToList(),
        };
    }

    private async Task<(
        IReadOnlyList<BugListSource> BugLists,
        ArtifactRepository? UpdatedRepository
    )> AnalyzeSingleRepository(
        ArtifactRepository repository,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        if (!await ValidateAndPrepareRepository(repository))
            return ([], null);

        var (owner, repoName) = urlProcessingService.ExtractGitHubOwnerAndRepo(repository.Url);
        var (bugLists, readmeRepos) = await PerformRepositoryAnalysis(
            owner,
            repoName,
            paper,
            cancellationToken
        );

        var updatedRepository = repository with
        {
            Confidence = repository.Confidence, // Pass through the original confidence for now
        };

        return (bugLists, updatedRepository);
    }

    private async Task<bool> ValidateAndPrepareRepository(ArtifactRepository repository)
    {
        if (!urlProcessingService.IsGitHubUrl(repository.Url))
        {
            logger.LogDebug("Skipping non-GitHub repository: {Url}", repository.Url);
            return false;
        }

        var (owner, repoName) = urlProcessingService.ExtractGitHubOwnerAndRepo(repository.Url);
        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repoName))
        {
            logger.LogWarning("Could not extract owner/repo from URL: {Url}", repository.Url);
            return false;
        }

        logger.LogDebug("Analyzing GitHub repository: {Owner}/{RepoName}", owner, repoName);

        if (!await VerifyRepositoryAccess(owner, repoName))
        {
            logger.LogWarning("Repository {Owner}/{RepoName} is not accessible", owner, repoName);
            return false;
        }

        return true;
    }

    private async Task<(
        IReadOnlyList<BugListSource> BugLists,
        IReadOnlyList<ArtifactRepository> ReadmeRepos
    )> PerformRepositoryAnalysis(
        string owner,
        string repoName,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        var bugLists = new List<BugListSource>();

        var analysisTask = AnalyzeRepositoryForBugLists(owner, repoName, cancellationToken);
        var readmeTask = AnalyzeRepositoryReadme(owner, repoName, paper, cancellationToken);

        await Task.WhenAll(analysisTask, readmeTask);

        bugLists.AddRange(await analysisTask);
        var (readmeBugs, readmeRepos) = await readmeTask;
        bugLists.AddRange(readmeBugs);

        return (bugLists.DistinctBy(b => b.Url).ToList(), readmeRepos);
    }

    private async Task<bool> VerifyRepositoryAccess(string owner, string repoName)
    {
        var repository = await gitHubClient.GetRepositoryInfoAsync(owner, repoName);
        return repository != null;
    }

    private async Task<IReadOnlyList<BugListSource>> AnalyzeRepositoryForBugLists(
        string owner,
        string repoName,
        CancellationToken cancellationToken
    )
    {
        var bugLists = new List<BugListSource>();
        var repository = await gitHubClient.GetRepositoryInfoAsync(owner, repoName);
        if (repository.HasIssues == true && repository.OpenIssuesCount > 0)
        {
            var issuesUrl = $"https://github.com/{owner}/{repoName}/issues";
            var confidence = ConfidenceCalculationService.CalculateIssueConfidence(
                repository.OpenIssuesCount.GetValueOrDefault()
            );
            bugLists.Add(
                new BugListSource
                {
                    Url = issuesUrl,
                    Type = "GitHub Issues",
                    DiscoveryMethod = "Repository Analysis",
                    Confidence = confidence,
                    TableContext = $"GitHub Issues ({repository.OpenIssuesCount} open issues)",
                }
            );
        }

        bugLists.AddRange(await AnalyzeBugRelatedPaths(owner, repoName, cancellationToken));
        return bugLists;
    }

    private async Task<IReadOnlyList<BugListSource>> AnalyzeBugRelatedPaths(
        string owner,
        string repoName,
        CancellationToken cancellationToken
    )
    {
        var bugLists = new List<BugListSource>();
        var tree = await GetRepositoryTreeWithRetry(owner, repoName, cancellationToken);
        if (tree?.Tree == null)
            return bugLists;

        foreach (var item in tree.Tree)
        {
            if (item.Type == "tree" && ContainsBugRelatedKeywords(item.Path))
            {
                var pathUrl = $"https://github.com/{owner}/{repoName}/tree/main/{item.Path}";
                bugLists.Add(
                    new BugListSource
                    {
                        Url = pathUrl,
                        Type = "Bug Directory",
                        DiscoveryMethod = "Repository Structure Analysis",
                        Confidence = BugDiscoveryConstants.RepositoryAnalysisConfidence * 0.8,
                        TableContext = $"Bug-related directory: {item.Path}",
                    }
                );
            }
        }
        return bugLists;
    }

    private static bool ContainsBugRelatedKeywords(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        return BugDiscoveryConstants.BugRelatedPaths.Any(keyword =>
            path.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        );
    }

    private async Task<RepositoryTree?> GetRepositoryTreeWithRetry(
        string owner,
        string repoName,
        CancellationToken cancellationToken
    )
    {
        for (int attempt = 1; attempt <= MaxRepositoryRetries; attempt++)
        {
            try
            {
                return await gitHubClient.GetRepositoryTreeAsync(owner, repoName);
            }
            catch (Exception ex) when (attempt < MaxRepositoryRetries)
            {
                logger.LogWarning(
                    ex,
                    "Repo tree request failed for {Owner}/{RepoName}, attempt {Attempt}/{MaxRetries}",
                    owner,
                    repoName,
                    attempt,
                    MaxRepositoryRetries
                );
                await Task.Delay(
                    BugDiscoveryConstants.RepositoryRetryDelayMs * attempt,
                    cancellationToken
                );
            }
        }
        logger.LogError(
            "Failed to get repo tree for {Owner}/{RepoName} after {MaxRetries} attempts",
            owner,
            repoName,
            MaxRepositoryRetries
        );
        return null;
    }

    private async Task<(
        IReadOnlyList<BugListSource> BugLists,
        IReadOnlyList<ArtifactRepository> Repositories
    )> AnalyzeRepositoryReadme(
        string owner,
        string repoName,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        var readmeContent = await gitHubClient.GetRepositoryReadmeAsync(owner, repoName);
        if (string.IsNullOrWhiteSpace(readmeContent))
            return ([], []);

        return ExtractUrlsFromReadme(readmeContent, owner, repoName);
    }

    private (
        IReadOnlyList<BugListSource> BugLists,
        IReadOnlyList<ArtifactRepository> Repositories
    ) ExtractUrlsFromReadme(string readmeContent, string owner, string repoName)
    {
        var bugLists = new List<BugListSource>();
        var repos = new List<ArtifactRepository>();
        var matches = UrlRegex().Matches(readmeContent);

        foreach (Match match in matches)
        {
            var url = CleanUrl(match.Value);
            if (IsBugTrackingUrl(url))
            {
                bugLists.Add(
                    new BugListSource
                    {
                        Url = url,
                        Type = DetermineBugListType(url),
                        DiscoveryMethod = "Repository README",
                        Confidence = BugDiscoveryConstants.RepositoryAnalysisConfidence,
                        TableContext = $"Found in {owner}/{repoName} README",
                    }
                );
            }
            else if (IsRepositoryUrl(url))
            {
                repos.Add(
                    new ArtifactRepository
                    {
                        Url = url,
                        Type = DetermineRepositoryType(url),
                        DiscoveryMethod = "Repository README",
                        Confidence = BugDiscoveryConstants.RepositoryAnalysisConfidence,
                    }
                );
            }
        }
        return (bugLists, repos);
    }

    private string CleanUrl(string url) =>
        url.TrimEnd('.', ',', ';', ')', ']', '}', ' ', '\t', '\n', '\r');

    [GeneratedRegex(@"https?://[^\s<>\)\]\}""']+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"https?://github\.com/[\w\-\.]+/[\w\-\.]+/issues", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubIssuesRegex();

    [GeneratedRegex(@"https?://bugs\.[\w\-\.]+", RegexOptions.IgnoreCase)]
    private static partial Regex BugsUrlRegex();

    [GeneratedRegex(@"https?://[\w\-\.]*jira[\w\-\.]*", RegexOptions.IgnoreCase)]
    private static partial Regex JiraUrlRegex();

    [GeneratedRegex(@"https?://[\w\-\.]*bugzilla[\w\-\.]*", RegexOptions.IgnoreCase)]
    private static partial Regex BugzillaUrlRegex();

    private bool IsBugTrackingUrl(string url)
    {
        return GitHubIssuesRegex().IsMatch(url)
            || BugsUrlRegex().IsMatch(url)
            || JiraUrlRegex().IsMatch(url)
            || BugzillaUrlRegex().IsMatch(url);
    }

    [GeneratedRegex(
        @"https?://github\.com/[\w\-\.]+/[\w\-\.]+(?!/issues|/wiki|/releases)",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex GitHubRepoRegex();

    [GeneratedRegex(@"https?://gitlab\.com/[\w\-\.]+/[\w\-\.]+", RegexOptions.IgnoreCase)]
    private static partial Regex GitLabRepoRegex();

    [GeneratedRegex(@"https?://bitbucket\.org/[\w\-\.]+/[\w\-\.]+", RegexOptions.IgnoreCase)]
    private static partial Regex BitbucketRepoRegex();

    [GeneratedRegex(@"https?://sourceforge\.net/projects/[\w\-\.]+", RegexOptions.IgnoreCase)]
    private static partial Regex SourceForgeRepoRegex();

    private bool IsRepositoryUrl(string url)
    {
        return GitHubRepoRegex().IsMatch(url)
            || GitLabRepoRegex().IsMatch(url)
            || BitbucketRepoRegex().IsMatch(url)
            || SourceForgeRepoRegex().IsMatch(url);
    }

    private static string DetermineRepositoryType(string url)
    {
        if (url.Contains("github.com"))
            return "GitHub";
        if (url.Contains("gitlab.com"))
            return "GitLab";
        if (url.Contains("bitbucket.org"))
            return "Bitbucket";
        if (url.Contains("sourceforge.net"))
            return "SourceForge";
        return "Repository";
    }

    private static string DetermineBugListType(string url)
    {
        if (url.Contains("github.com"))
            return "GitHub Issues";
        if (url.Contains("jira"))
            return "Jira";
        if (url.Contains("bugzilla"))
            return "Bugzilla";
        return "Bug Tracker";
    }
}
