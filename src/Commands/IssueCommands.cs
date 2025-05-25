using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ConsoleAppFramework;
using DataCollection.Models.IssueTracker;
using DataCollection.Options;
using DataCollection.Services;
using EnumsNET;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Octokit;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace DataCollection.Commands;

[RegisterCommands("issue")]
[ConsoleAppFilter<PathsOptions.Filter>]
[ConsoleAppFilter<CredentialOptions.Filter>]
public class IssueCommands(
    ILogger<IssueCommands> logger,
    GitHubService gitHubService,
    IssueOverallStatusCriterion statusCriterion,
    IOptions<PathsOptions> pathsOptions
)
{
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Analyzes an issue and determines its status.
    /// </summary>
    /// <param name="url">Optional URL to the GitHub issue</param>
    /// <param name="owner">The owner of the repository (if URL not provided)</param>
    /// <param name="repoName">The name of the repository (if URL not provided)</param>
    /// <param name="issueNumber">The number of the issue (if URL not provided)</param>
    /// <param name="saveResults">Whether to save analysis results to disk</param>
    /// <param name="useCache">Whether to use cached analysis results if available</param>
    public async Task DecideStatus(
        string? url,
        string? owner = null,
        string? repoName = null,
        long? issueNumber = null,
        bool saveResults = false,
        bool useCache = true
    )
    {
        if (url != null)
        {
            var isUri = Uri.TryCreate(url, UriKind.Absolute, out var uri);
            if (!isUri || uri == null)
            {
                logger.LogError("Invalid URL format: {Url}", url);
                return;
            }

            switch (uri.Segments.Length)
            {
                case < 5:
                    logger.LogError(
                        "Invalid GitHub issue URL format: {Url}. "
                            + "Expected at least 5 segments.",
                        url
                    );
                    return;
                case >= 5:
                    owner = uri.Segments[1].TrimEnd('/');
                    repoName = uri.Segments[2].TrimEnd('/');
                    var shouldBeIssueNumber = uri.Segments[4].TrimEnd('/');
                    if (!long.TryParse(shouldBeIssueNumber, out var parsedIssueNumber))
                    {
                        logger.LogError(
                            "Could not parse issue number from URL segment: {Segment}",
                            shouldBeIssueNumber
                        );
                        return;
                    }
                    issueNumber = parsedIssueNumber;
                    break;
            }
        }

        if (
            string.IsNullOrWhiteSpace(owner)
            || string.IsNullOrWhiteSpace(repoName)
            || !issueNumber.HasValue
        )
        {
            logger.LogError(
                "Insufficient information: Owner, RepoName, and IssueNumber must be provided if URL is not set."
            );
            return;
        }

        if (useCache)
        {
            var cachedResult = await TryGetCachedAnalysisResultAsync(
                owner,
                repoName,
                issueNumber.Value
            );
            if (cachedResult != null)
            {
                logger.LogInformation(
                    "{Owner}/{Repo}#{IssueNumber} is cached. Status: {Status} {StatusDescription} {StatusExplanation}",
                    owner,
                    repoName,
                    issueNumber.Value,
                    cachedResult.Status.GetName(),
                    cachedResult.Status.AsString(EnumFormat.Description),
                    cachedResult.Analysis.NuanceOrExplanation
                );
                return;
            }
        }

        logger.LogInformation(
            "Deciding status for issue: {Owner}/{Repo}#{IssueNumber}",
            owner,
            repoName,
            issueNumber.Value
        );
        try
        {
            var (status, analysisResult) = await DecideStatus(owner, repoName, issueNumber.Value);
            if (saveResults)
                await SaveAnalysisResult(
                    owner,
                    repoName,
                    issueNumber.Value,
                    status,
                    analysisResult
                );
        }
        catch (ApiException apiEx)
        {
            logger.LogError(
                apiEx,
                "Failed to decide status for issue {Owner}/{Repo}#{IssueNumber}.",
                owner,
                repoName,
                issueNumber.Value
            );
        }
    }

    /// <summary>
    /// Process a batch of issues from a file
    /// </summary>
    /// <param name="inputFile">Path to a file containing issue URLs or owner/repo/issue combinations, one per line</param>
    /// <param name="saveResults">Whether to save analysis results to disk</param>
    /// <param name="useCache">Whether to use cached results if available</param>
    /// <param name="maxParallelTasks">Maximum number of parallel tasks (default 10 to avoid GitHub API rate limits)</param>
    public async Task ProcessBatch(
        string inputFile,
        bool saveResults = true,
        bool useCache = true,
        int maxParallelTasks = 10
    )
    {
        if (!File.Exists(inputFile))
        {
            logger.LogError("Input file not found: {InputFile}", inputFile);
            return;
        }

        var lines = await File.ReadAllLinesAsync(inputFile);
        logger.LogInformation(
            "Processing {Count} issues from {InputFile}",
            lines.Length,
            inputFile
        );

        // Collect all valid issues
        var issueTasks = new List<(string? Url, string? Owner, string? Repo, long? Number)>();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
                continue;

            if (trimmedLine.StartsWith("http"))
            {
                // Process as URL
                issueTasks.Add((trimmedLine, null, null, null));
            }
            else
            {
                // Process as owner/repo/issue format
                var parts = trimmedLine.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && long.TryParse(parts[2], out var issueNum))
                    issueTasks.Add((null, parts[0], parts[1], issueNum));
                else
                    logger.LogWarning("Invalid issue format: {Line}", trimmedLine);
            }
        }

        logger.LogInformation(
            "Found {ValidCount} valid issues out of {TotalCount}",
            issueTasks.Count,
            lines.Length
        );

        // Process issues in parallel with rate limiting
        var semaphore = new SemaphoreSlim(maxParallelTasks);
        var tasks = new List<Task>();

        foreach (var issue in issueTasks)
        {
            await semaphore.WaitAsync();

            tasks.Add(
                Task.Run(async () =>
                {
                    try
                    {
                        await DecideStatus(
                            url: issue.Url,
                            owner: issue.Owner,
                            repoName: issue.Repo,
                            issueNumber: issue.Number,
                            saveResults: saveResults,
                            useCache: useCache
                        );
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                })
            );
        }

        await Task.WhenAll(tasks);
        logger.LogInformation(
            "Completed parallel batch processing of {Count} issues",
            issueTasks.Count
        );
    }

    private async Task<(IssueStatus, IssueAnalysisResponse)> DecideStatus(
        string owner,
        string repoName,
        long issueNumber
    )
    {
        // Check if we have a cached repository profile
        await EnsureRepoCached(owner, repoName);

        var issueProfile = await gitHubService.BuildComprehensiveIssueProfileAsync(
            owner,
            repoName,
            issueNumber
        );

        await CacheUserProfiles(issueProfile);

        var analysisResult = await statusCriterion.EvaluateAsync(issueProfile);

        logger.LogDebug("Analysis: {AnalysisResult}", analysisResult);

        IssueStatus currentStatus;

        if (analysisResult.IsDuplicate == true)
            currentStatus = IssueStatus.Duplicate;
        else if (analysisResult.IsFixedBeforeIssueRaised == true)
            currentStatus = IssueStatus.FixedBeforeReport;
        else if (analysisResult.IsRealBug == false)
            currentStatus = IssueStatus.NotABug;
        else if (issueProfile.IsClosed)
            if (analysisResult.IsFixed == true)
                currentStatus = IssueStatus.ConfirmedFixed;
            else
                currentStatus =
                    analysisResult.IsBugButWontFix == true
                        ? IssueStatus.ConfirmedWontFix
                        : IssueStatus.Pending;
        else
            currentStatus = IssueStatus.ConfirmedWaitingForAction;

        logger.LogInformation(
            "Issue status decision process complete for {Owner}/{Repo}#{IssueNumber}. Concluded {StatusName} {StatusMessage}. LLM Explanation: {Explanation}",
            owner,
            repoName,
            issueNumber,
            currentStatus.GetName(),
            currentStatus.AsString(EnumFormat.Description),
            analysisResult.NuanceOrExplanation
        );

        return (currentStatus, analysisResult);
    }

    private async Task SaveAnalysisResult(
        string owner,
        string repoName,
        long issueNumber,
        IssueStatus status,
        IssueAnalysisResponse analysisResult
    )
    {
        var repoDir = Path.Combine(pathsOptions.Value.IssueAnalysisDir, owner, repoName);
        Directory.CreateDirectory(repoDir);

        var resultFileName = Path.Combine(repoDir, $"issue_{issueNumber}.json");

        var resultObject = new
        {
            Owner = owner,
            Repository = repoName,
            IssueNumber = issueNumber,
            Status = status.ToString(),
            StatusDescription = status.AsString(EnumFormat.Description),
            Analysis = analysisResult,
        };

        var json = JsonSerializer.Serialize(resultObject, jsonOptions);
        await File.WriteAllTextAsync(resultFileName, json);

        logger.LogInformation("Saved analysis result to {ResultFileName}", resultFileName);
    }

    // Class to represent cached analysis results
    private class CachedAnalysisResult
    {
        public required string Owner { get; set; }
        public required string Repository { get; set; }
        public required long IssueNumber { get; set; }
        public required IssueStatus Status { get; set; }
        public required IssueAnalysisResponse Analysis { get; set; }
    }

    private async Task<CachedAnalysisResult?> TryGetCachedAnalysisResultAsync(
        string owner,
        string repoName,
        long issueNumber
    )
    {
        var resultFile = Path.Combine(
            pathsOptions.Value.IssueAnalysisDir,
            owner,
            repoName,
            $"issue_{issueNumber}.json"
        );

        if (!File.Exists(resultFile))
            return null;
        try
        {
            var json = await File.ReadAllTextAsync(resultFile);
            return JsonConvert.DeserializeObject<CachedAnalysisResult?>(json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to read cached analysis for {Owner}/{Repo}#{IssueNumber}",
                owner,
                repoName,
                issueNumber
            );
        }

        return null;
    }

    private async Task EnsureRepoCached(string owner, string repoName)
    {
        var repoDir = Path.Combine(pathsOptions.Value.IssueRepoDir, owner);
        Directory.CreateDirectory(repoDir);

        await gitHubService.GetRepositoryInfoAsync(owner, repoName);
    }

    private async Task CacheUserProfiles(IssueProfile issueProfile)
    {
        // Extract repository info from issueProfile
        var repoFullName = issueProfile.RepositoryFullName;
        var parts = repoFullName.Split('/');
        var owner = parts[0];
        var repoName = parts[1];
        var repository = await gitHubService.GetRepositoryInfoAsync(owner, repoName);

        var userLogins = new HashSet<string> { issueProfile.OctokitIssue.User.Login };

        foreach (var comment in issueProfile.CommentEvents)
            userLogins.Add(comment.By.Login);

        foreach (var label in issueProfile.LabelEvents)
            userLogins.Add(label.By.Login);

        foreach (var login in userLogins)
        {
            try
            {
                await gitHubService.GetUserProfileAsync(login, repository);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to cache user profile for {Login} in repository {RepoId}",
                    login,
                    repository.Id
                );
            }
        }
    }
}
