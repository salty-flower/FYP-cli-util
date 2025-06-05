using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataCollection.Models.IssueTracker.Criteria;
using DataCollection.Models.IssueTracker.Profiles;
using DataCollection.Models.IssueTracker.Responses;
using EnumsNET;
using Microsoft.Extensions.Logging;

namespace DataCollection.Services;

public class IssueBatchProcessingService(
    ILogger<IssueBatchProcessingService> logger,
    GitHubService gitHubService,
    IssueOverallStatusCriterion statusCriterion,
    SingleIssueProcessingService singleIssueService,
    DatabaseIssueAnalysisStorageService storageService
)
{
    /// <summary>
    /// Processes a batch of issues using OpenAI Batch API
    /// </summary>
    public async Task ProcessBatchWithOpenAIAsync(
        List<(string? Url, string? Owner, string? Repo, long? Number)> issueTasks,
        bool saveResults,
        bool useCache,
        string? batchJobId = null
    )
    {
        logger.LogInformation(
            "Using OpenAI Batch API for processing {Count} issues",
            issueTasks.Count
        );

        // If batch job ID is provided, skip to polling and result processing
        if (!string.IsNullOrEmpty(batchJobId))
        {
            logger.LogInformation("Resuming existing batch job: {BatchJobId}", batchJobId);
            await ProcessExistingBatchJobAsync(batchJobId, issueTasks, saveResults, useCache);
            return;
        }

        // Continue with normal batch processing for new jobs
        // First, build issue profiles for all issues
        var issueProfiles = new Dictionary<string, IssueProfile>();
        var issueMetadata = new Dictionary<string, (string Owner, string Repo, long Number)>();

        foreach (var issue in issueTasks)
        {
            try
            {
                // Parse issue details
                var (owner, repoName, issueNumber) = ParseIssueDetailsAsync(issue);
                if (owner == null || repoName == null || !issueNumber.HasValue)
                    continue;

                // Check cache first if enabled
                if (useCache)
                {
                    var cachedResult = await storageService.TryGetCachedAnalysisResultAsync(
                        owner,
                        repoName,
                        issueNumber.Value
                    );
                    if (cachedResult != null)
                    {
                        logger.LogInformation(
                            "{Owner}/{Repo}#{IssueNumber} is cached. Status: {Status} {StatusDescription}",
                            owner,
                            repoName,
                            issueNumber.Value,
                            cachedResult.Status.GetName(),
                            cachedResult.Status.AsString(EnumFormat.Description)
                        );
                        continue;
                    }
                }

                // Build issue profile using the single issue service helper methods
                // We don't call ProcessIssueAsync here because we want to batch the analysis
                var issueProfile = await gitHubService.BuildComprehensiveIssueProfileAsync(
                    owner,
                    repoName,
                    issueNumber.Value
                );

                var customId = $"{owner}/{repoName}#{issueNumber.Value}";
                issueProfiles[customId] = issueProfile;
                issueMetadata[customId] = (owner, repoName, issueNumber.Value);

                logger.LogDebug("Prepared issue profile for {CustomId}", customId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to prepare issue profile for {Issue}", issue);
            }
        }

        if (issueProfiles.Count == 0)
        {
            logger.LogWarning("No issue profiles to process");
            return;
        }

        logger.LogInformation("Submitting {Count} issues to OpenAI Batch API", issueProfiles.Count);

        try
        {
            // Process using batch API
            var batchResults = await statusCriterion.EvaluateBatchAsync(issueProfiles);

            logger.LogInformation(
                "Received {Count} results from batch processing",
                batchResults.Count
            );

            // Process results and save if requested
            await ProcessBatchResultsAsync(batchResults, issueMetadata, issueProfiles, saveResults);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process batch with OpenAI Batch API");
            throw;
        }

        logger.LogInformation(
            "Completed batch processing of {Count} issues using OpenAI Batch API",
            issueProfiles.Count
        );
    }

    /// <summary>
    /// Processes a batch of issues using parallel processing
    /// </summary>
    public async Task ProcessBatchWithParallelismAsync(
        List<(string? Url, string? Owner, string? Repo, long? Number)> issueTasks,
        bool saveResults,
        bool useCache,
        int maxParallelTasks = 10
    )
    {
        logger.LogInformation("Using parallel processing for {Count} issues", issueTasks.Count);

        // Process issues in parallel with rate limiting
        var semaphore = new SemaphoreSlim(maxParallelTasks);
        var tasks = new List<Task>();

        foreach (var (Url, Owner, Repo, Number) in issueTasks)
        {
            await semaphore.WaitAsync();

            tasks.Add(
                Task.Run(async () =>
                {
                    try
                    {
                        var (owner, repoName, issueNumber) = ParseIssueDetailsAsync(
                            (Url, Owner, Repo, Number)
                        );

                        if (owner != null && repoName != null && issueNumber.HasValue)
                        {
                            await singleIssueService.ProcessIssueAsync(
                                owner,
                                repoName,
                                issueNumber.Value,
                                useCache: useCache,
                                saveResults: saveResults
                            );
                        }
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

    /// <summary>
    /// Processes an existing batch job
    /// </summary>
    private async Task ProcessExistingBatchJobAsync(
        string batchJobId,
        List<(string? Url, string? Owner, string? Repo, long? Number)> issueTasks,
        bool saveResults,
        bool useCache
    )
    {
        logger.LogInformation("Processing existing batch job: {BatchJobId}", batchJobId);

        try
        {
            // Use the batch criterion to poll for completion and get results
            var batchResults = await statusCriterion.ResumeBatchAsync(batchJobId);

            logger.LogInformation(
                "Received {Count} results from existing batch job",
                batchResults.Count
            );

            // Build issue metadata for result processing
            var issueMetadata = new Dictionary<string, (string Owner, string Repo, long Number)>();
            var issueProfiles = new Dictionary<string, IssueProfile>();

            foreach (var issue in issueTasks)
            {
                try
                {
                    var (owner, repoName, issueNumber) = ParseIssueDetailsAsync(issue);
                    if (owner == null || repoName == null || !issueNumber.HasValue)
                        continue;

                    // Check cache first if enabled
                    if (useCache)
                    {
                        var cachedResult = await storageService.TryGetCachedAnalysisResultAsync(
                            owner,
                            repoName,
                            issueNumber.Value
                        );
                        if (cachedResult != null)
                        {
                            logger.LogInformation(
                                "{Owner}/{Repo}#{IssueNumber} is cached. Status: {Status} {StatusDescription}",
                                owner,
                                repoName,
                                issueNumber.Value,
                                cachedResult.Status.GetName(),
                                cachedResult.Status.AsString(EnumFormat.Description)
                            );
                            continue;
                        }
                    }

                    var customId = $"{owner}/{repoName}#{issueNumber.Value}";
                    issueMetadata[customId] = (owner, repoName, issueNumber.Value);

                    // Build issue profile for status determination
                    var issueProfile = await gitHubService.BuildComprehensiveIssueProfileAsync(
                        owner,
                        repoName,
                        issueNumber.Value
                    );
                    issueProfiles[customId] = issueProfile;

                    logger.LogDebug("Prepared issue metadata for {CustomId}", customId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to prepare issue metadata for {Issue}", issue);
                }
            }

            // Process results and save if requested
            await ProcessBatchResultsAsync(batchResults, issueMetadata, issueProfiles, saveResults);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process existing batch job {BatchJobId}", batchJobId);
            throw;
        }

        logger.LogInformation(
            "Completed processing of existing batch job {BatchJobId}",
            batchJobId
        );
    }

    /// <summary>
    /// Processes batch results and saves them if requested
    /// </summary>
    private async Task ProcessBatchResultsAsync(
        Dictionary<string, IssueAnalysisResponse> batchResults,
        Dictionary<string, (string Owner, string Repo, long Number)> issueMetadata,
        Dictionary<string, IssueProfile> issueProfiles,
        bool saveResults
    )
    {
        foreach (var (customId, analysisResult) in batchResults)
        {
            if (!issueMetadata.TryGetValue(customId, out var metadata))
            {
                logger.LogWarning("No metadata found for custom ID: {CustomId}", customId);
                continue;
            }

            if (!issueProfiles.TryGetValue(customId, out var issueProfile))
            {
                logger.LogWarning("No issue profile found for custom ID: {CustomId}", customId);
                continue;
            }

            var (owner, repoName, issueNumber) = metadata;

            // Determine status using the same logic as the regular method
            var currentStatus = SingleIssueProcessingService.DetermineIssueStatus(
                analysisResult,
                issueProfile
            );

            logger.LogInformation(
                "Issue status decision complete for {Owner}/{Repo}#{IssueNumber}. Concluded {StatusName} {StatusMessage}. LLM Explanation: {Explanation}",
                owner,
                repoName,
                issueNumber,
                currentStatus.GetName(),
                currentStatus.AsString(EnumFormat.Description),
                analysisResult.NuanceOrExplanation
            );

            if (saveResults)
            {
                await storageService.SaveAnalysisResultAsync(
                    owner,
                    repoName,
                    issueNumber,
                    currentStatus,
                    analysisResult
                );
            }
        }
    }

    /// <summary>
    /// Parses issue details from various input formats
    /// </summary>
    private (string? Owner, string? RepoName, long? IssueNumber) ParseIssueDetailsAsync(
        (string? Url, string? Owner, string? Repo, long? Number) issue
    )
    {
        string? owner,
            repoName;
        long? issueNumber;

        if (issue.Url != null)
        {
            var isUri = Uri.TryCreate(issue.Url, UriKind.Absolute, out var uri);
            if (!isUri || uri == null || uri.Segments.Length < 5)
            {
                logger.LogWarning("Invalid GitHub issue URL format: {Url}", issue.Url);
                return (null, null, null);
            }

            owner = uri.Segments[1].TrimEnd('/');
            repoName = uri.Segments[2].TrimEnd('/');
            var shouldBeIssueNumber = uri.Segments[4].TrimEnd('/');
            if (!long.TryParse(shouldBeIssueNumber, out var parsedIssueNumber))
            {
                logger.LogWarning("Could not parse issue number from URL: {Url}", issue.Url);
                return (null, null, null);
            }
            issueNumber = parsedIssueNumber;
        }
        else
        {
            owner = issue.Owner;
            repoName = issue.Repo;
            issueNumber = issue.Number;
        }

        return (owner, repoName, issueNumber);
    }
}
