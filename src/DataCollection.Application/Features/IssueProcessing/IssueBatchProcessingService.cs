using DataCollection.Application.Common.Services;
using DataCollection.Application.Features.BugDiscovery;
using DataCollection.Application.Features.IssueAnalysis.Rules;
using DataCollection.Application.Models.IssueTracker.Profiles;
using DataCollection.Core.Models.IssueTracker.Responses;
using DataCollection.Infrastructure.Options;
using EnumsNET;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Application.Features.IssueProcessing;

public class IssueBatchProcessingService(
    ILogger<IssueBatchProcessingService> logger,
    GitHubService gitHubService,
    IssueSubjectiveStatusCriterion statusCriterion,
    SingleIssueProcessingService singleIssueService,
    DatabaseIssueAnalysisStorageService storageService,
    IOptions<ParallelismOptions> parallelismOptions
)
{
    /// <summary>
    /// Processes a batch of issues using OpenAI Batch API
    /// </summary>
    public async Task<List<IssueAnalysisResult>> ProcessBatchWithOpenAIAsync(
        List<(string? Url, string? Owner, string? Repo, long? Number)> issueTasks,
        bool saveResults,
        bool useCache,
        string? batchJobId = null,
        CancellationToken cancellationToken = default
    )
    {
        logger.LogInformation(
            "Using OpenAI Batch API for processing {Count} issues",
            issueTasks.Count
        );

        if (!string.IsNullOrEmpty(batchJobId))
        {
            return await ProcessExistingBatchJobAsync(
                batchJobId,
                issueTasks,
                saveResults,
                useCache,
                cancellationToken
            );
        }

        var (issueMetadata, issueProfiles) = await PrepareIssueData(
            issueTasks,
            useCache,
            cancellationToken
        );
        var batchResults = await ExecuteBatchProcessing(issueProfiles, cancellationToken);
        var results = await ProcessBatchResultsAsync(
            batchResults,
            issueMetadata,
            issueProfiles,
            saveResults,
            cancellationToken
        );

        logger.LogInformation("Completed batch processing of {Count} issues", issueProfiles.Count);
        return results;
    }

    /// <summary>
    /// Processes a batch of issues using parallel processing
    /// </summary>
    public async Task<List<IssueAnalysisResult>> ProcessBatchWithParallelismAsync(
        List<(string? Url, string? Owner, string? Repo, long? Number)> issueTasks,
        bool saveResults,
        bool useCache,
        CancellationToken cancellationToken = default
    )
    {
        var effectiveParallelTasks = parallelismOptions.Value.IssueProcessing;
        logger.LogInformation(
            "Using parallel processing for {Count} issues with {MaxParallel} max parallel tasks",
            issueTasks.Count,
            effectiveParallelTasks
        );

        var semaphore = new SemaphoreSlim(effectiveParallelTasks);
        var tasks = issueTasks.Select(issue =>
            ProcessSingleIssueWithSemaphore(
                issue,
                saveResults,
                useCache,
                semaphore,
                cancellationToken
            )
        );
        var results = await Task.WhenAll(tasks);

        logger.LogInformation(
            "Completed parallel batch processing of {Count} issues",
            issueTasks.Count
        );

        return results.OfType<IssueAnalysisResult>().ToList();
    }

    private async Task<IssueAnalysisResult?> ProcessSingleIssueWithSemaphore(
        (string? Url, string? Owner, string? Repo, long? Number) issue,
        bool saveResults,
        bool useCache,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken
    )
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var (owner, repoName, issueNumber) = ParseIssueDetails(issue);

            if (owner != null && repoName != null && issueNumber.HasValue)
            {
                var result = await singleIssueService.ProcessIssueAsync(
                    owner,
                    repoName,
                    issueNumber.Value,
                    useCache: useCache,
                    saveResults: saveResults
                );

                if (result.HasValue)
                {
                    return new IssueAnalysisResult
                    {
                        Owner = owner,
                        Repo = repoName,
                        IssueNumber = issueNumber.Value,
                        Url = $"https://github.com/{owner}/{repoName}/issues/{issueNumber.Value}",
                        Analysis = result.Value.Analysis,
                    };
                }
            }

            return null;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Processes an existing batch job
    /// </summary>
    private async Task<List<IssueAnalysisResult>> ProcessExistingBatchJobAsync(
        string batchJobId,
        List<(string? Url, string? Owner, string? Repo, long? Number)> issueTasks,
        bool saveResults,
        bool useCache,
        CancellationToken cancellationToken
    )
    {
        logger.LogInformation("Processing existing batch job: {BatchJobId}", batchJobId);

        try
        {
            var subjectiveResults = await statusCriterion.ResumeBatchAsync(
                batchJobId,
                cancellationToken
            );
            logger.LogInformation(
                "Received {Count} subjective results from existing batch job",
                subjectiveResults.Count
            );

            var (issueMetadata, issueProfiles) = await PrepareIssueData(
                issueTasks,
                useCache,
                cancellationToken
            );

            var mergedResults = MergeSubjectiveWithDeterministic(subjectiveResults, issueProfiles);

            var results = await ProcessBatchResultsAsync(
                mergedResults,
                issueMetadata,
                issueProfiles,
                saveResults,
                cancellationToken
            );

            logger.LogInformation(
                "Completed processing of existing batch job {BatchJobId}",
                batchJobId
            );

            return results;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process existing batch job {BatchJobId}", batchJobId);
            throw;
        }
    }

    /// <summary>
    /// Processes batch results and saves them if requested
    /// </summary>
    private async Task<List<IssueAnalysisResult>> ProcessBatchResultsAsync(
        Dictionary<string, IssueAnalysisResponse> batchResults,
        Dictionary<string, (string Owner, string Repo, long Number)> issueMetadata,
        Dictionary<string, IssueProfile> issueProfiles,
        bool saveResults,
        CancellationToken cancellationToken
    )
    {
        var maxParallel = parallelismOptions.Value.BatchResultsProcessing;
        logger.LogInformation(
            "Processing {Count} batch results with {MaxParallel} parallel tasks",
            batchResults.Count,
            maxParallel
        );

        var results = new List<IssueAnalysisResult>();
        var resultsLock = new object();
        var semaphore = new SemaphoreSlim(maxParallel);
        var processingTasks = batchResults.Select(async kvp =>
        {
            var (customId, analysisResult) = kvp;
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                if (!issueMetadata.TryGetValue(customId, out var metadata))
                {
                    logger.LogWarning("No metadata found for custom ID: {CustomId}", customId);
                    return;
                }

                if (!issueProfiles.TryGetValue(customId, out var issueProfile))
                {
                    logger.LogWarning("No issue profile found for custom ID: {CustomId}", customId);
                    return;
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
                    analysisResult.Deterministic?.NuanceOrExplanation
                );

                if (saveResults)
                {
                    await storageService.SaveAnalysisResultAsync(
                        owner,
                        repoName,
                        issueNumber,
                        currentStatus,
                        analysisResult,
                        cancellationToken
                    );
                }

                // Collect result for export
                var result = new IssueAnalysisResult
                {
                    Owner = owner,
                    Repo = repoName,
                    IssueNumber = issueNumber,
                    Url = $"https://github.com/{owner}/{repoName}/issues/{issueNumber}",
                    Analysis = analysisResult,
                };

                lock (resultsLock)
                {
                    results.Add(result);
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(processingTasks);
        logger.LogInformation("Completed processing all batch results");
        return results;
    }

    private async Task<(
        Dictionary<string, (string, string, long)>,
        Dictionary<string, IssueProfile>
    )> PrepareIssueData(
        List<(string? Url, string? Owner, string? Repo, long? Number)> issueTasks,
        bool useCache,
        CancellationToken cancellationToken
    )
    {
        var issueProfiles = new Dictionary<string, IssueProfile>();
        var issueMetadata = new Dictionary<string, (string Owner, string Repo, long Number)>();
        var profileLock = new object();
        var metadataLock = new object();

        var maxParallel = parallelismOptions.Value.IssueDataPreparation;
        logger.LogInformation(
            "Preparing issue data with {MaxParallel} parallel tasks",
            maxParallel
        );

        var semaphore = new SemaphoreSlim(maxParallel);
        var preparationTasks = issueTasks.Select(async issue =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var (owner, repoName, issueNumber) = ParseIssueDetails(issue);
                if (owner == null || repoName == null || !issueNumber.HasValue)
                    return;

                // Check cache first if enabled
                if (
                    useCache
                    && await IsIssueCached(owner, repoName, issueNumber.Value, cancellationToken)
                )
                    return;

                // Build issue profile
                var issueProfile = await gitHubService.BuildComprehensiveIssueProfileAsync(
                    owner,
                    repoName,
                    issueNumber.Value,
                    cancellationToken
                );
                if (issueProfile == null)
                {
                    logger.LogWarning(
                        "No issue profile found for {Owner}/{Repo}#{IssueNumber}",
                        owner,
                        repoName,
                        issueNumber.Value
                    );
                    return;
                }

                var customId = $"{owner}/{repoName}#{issueNumber.Value}";

                lock (profileLock)
                {
                    issueProfiles[customId] = issueProfile;
                }

                lock (metadataLock)
                {
                    issueMetadata[customId] = (owner, repoName, issueNumber.Value);
                }

                logger.LogDebug("Prepared issue profile for {CustomId}", customId);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(preparationTasks);

        if (issueProfiles.Count == 0)
        {
            logger.LogWarning("No issue profiles to process");
        }
        else
        {
            logger.LogInformation(
                "Successfully prepared {Count} issue profiles",
                issueProfiles.Count
            );
        }

        return (issueMetadata, issueProfiles);
    }

    private async Task<bool> IsIssueCached(
        string owner,
        string repoName,
        long issueNumber,
        CancellationToken cancellationToken
    )
    {
        var cachedResult = await storageService.TryGetCachedAnalysisResultAsync(
            owner,
            repoName,
            issueNumber,
            cancellationToken
        );

        if (cachedResult != null)
        {
            logger.LogInformation(
                "{Owner}/{Repo}#{IssueNumber} is cached. Status: {Status} {StatusDescription}",
                owner,
                repoName,
                issueNumber,
                cachedResult.Status.GetName(),
                cachedResult.Status.AsString(EnumFormat.Description)
            );
            return true;
        }

        return false;
    }

    private async Task<Dictionary<string, IssueAnalysisResponse>> ExecuteBatchProcessing(
        Dictionary<string, IssueProfile> issueProfiles,
        CancellationToken cancellationToken
    )
    {
        logger.LogInformation("Submitting {Count} issues to OpenAI Batch API", issueProfiles.Count);

        var subjectiveResults = await statusCriterion.EvaluateBatchAsync(
            issueProfiles,
            cancellationToken
        );

        logger.LogInformation(
            "Received {Count} subjective results from batch processing",
            subjectiveResults.Count
        );

        return MergeSubjectiveWithDeterministic(subjectiveResults, issueProfiles);
    }

    private Dictionary<string, IssueAnalysisResponse> MergeSubjectiveWithDeterministic(
        Dictionary<string, SubjectiveIssueAnalysis> subjectiveResults,
        Dictionary<string, IssueProfile> issueProfiles
    )
    {
        var mergedResults = new Dictionary<string, IssueAnalysisResponse>();
        foreach (var (customId, subjective) in subjectiveResults)
        {
            if (!issueProfiles.TryGetValue(customId, out var profile))
            {
                logger.LogWarning("No profile found for custom ID: {CustomId}", customId);
                continue;
            }

            var deterministic = gitHubService.SynthesizeDeterministicIssueAnalysis(profile);
            mergedResults[customId] = new IssueAnalysisResponse
            {
                Deterministic = deterministic,
                Subjective = subjective,
            };
        }

        logger.LogInformation("Merged {Count} analysis results", mergedResults.Count);
        return mergedResults;
    }

    /// <summary>
    /// Parses issue details from various input formats
    /// </summary>
    private (string? Owner, string? RepoName, long? IssueNumber) ParseIssueDetails(
        (string? Url, string? Owner, string? Repo, long? Number) issue
    )
    {
        if (issue.Url != null)
        {
            return UrlProcessingService.ParseGitHubIssueUrl(issue.Url, logger);
        }

        return (issue.Owner, issue.Repo, issue.Number);
    }
}
