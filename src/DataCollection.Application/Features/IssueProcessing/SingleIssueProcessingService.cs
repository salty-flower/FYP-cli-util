using DataCollection.Application.Features.BugDiscovery;
using DataCollection.Application.Features.IssueAnalysis.Rules;
using DataCollection.Application.Models.IssueTracker.Profiles;
using DataCollection.Core.Models.IssueTracker;
using DataCollection.Core.Models.IssueTracker.Responses;
using DataCollection.Infrastructure.Clients.IssueTrackers;
using DataCollection.Infrastructure.Options;
using EnumsNET;
using GitHub.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;

namespace DataCollection.Application.Features.IssueProcessing;

public class SingleIssueProcessingService(
    ILogger<SingleIssueProcessingService> logger,
    GitHubService gitHubService,
    IGitHubClient gitHubClient,
    IssueSubjectiveStatusCriterion statusCriterion,
    DatabaseIssueAnalysisStorageService storageService,
    IOptions<PathsOptions> pathsOptions
)
{
    /// <summary>
    /// Processes a single issue and returns the status and analysis result.
    /// Responsibilities are split into small helpers for readability and testability.
    /// </summary>
    public async Task<(IssueStatus Status, IssueAnalysisResponse Analysis)?> ProcessIssueAsync(
        string owner,
        string repoName,
        long issueNumber,
        bool useCache = true,
        bool saveResults = false,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(repoName);
        if (issueNumber <= 0)
            throw new ArgumentOutOfRangeException(nameof(issueNumber));

        // Try cached result early
        if (useCache)
        {
            var cached = await TryGetCachedResultAsync(
                owner,
                repoName,
                issueNumber,
                cancellationToken
            );
            if (cached is not null)
            {
                logger.LogInformation(
                    "{Owner}/{Repo}#{IssueNumber} is cached. Status: {Status} {StatusDescription} {StatusExplanation}",
                    owner,
                    repoName,
                    issueNumber,
                    cached.Status.GetName(),
                    cached.Status.AsString(EnumFormat.Description),
                    cached.Analysis.Deterministic.NuanceOrExplanation
                );
                return (cached.Status, cached.Analysis);
            }
        }

        logger.LogInformation(
            "Processing issue: {Owner}/{Repo}#{IssueNumber}",
            owner,
            repoName,
            issueNumber
        );

        // Build profile
        var profile = await FetchIssueProfileAsync(owner, repoName, issueNumber, cancellationToken);
        if (profile is null)
        {
            logger.LogWarning(
                "No issue profile found for {Owner}/{Repo}#{IssueNumber}",
                owner,
                repoName,
                issueNumber
            );
            return null;
        }

        // Cache user profiles (parallelized)
        await CacheUserProfilesAsync(profile, cancellationToken);

        // Deterministic synthesis + LLM subjective evaluation
        var deterministic = gitHubService.SynthesizeDeterministicIssueAnalysis(profile);
        var subjective = await statusCriterion.EvaluateAsync(profile);
        var analysisResult = new IssueAnalysisResponse
        {
            Deterministic = deterministic,
            Subjective = subjective,
        };

        // Ensure subjective confidences are sane
        SanitizeSubjectiveConfidences(analysisResult);

        logger.LogDebug("Analysis: {AnalysisResult}", analysisResult);

        // Determine final status
        var currentStatus = DetermineIssueStatus(analysisResult, profile);

        var (statusName, statusDesc) = FormatStatusForLog(currentStatus);
        logger.LogInformation(
            "Issue status decision process complete for {Owner}/{Repo}#{IssueNumber}. Concluded {StatusName} {StatusMessage}. Final Explanation: {Explanation}",
            owner,
            repoName,
            issueNumber,
            statusName,
            statusDesc,
            analysisResult.Deterministic.NuanceOrExplanation
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

        return (currentStatus, analysisResult);
    }

    /// <summary>
    /// Tries to fetch a cached analysis result from storage.
    /// </summary>
    private async Task<CachedAnalysisResult?> TryGetCachedResultAsync(
        string owner,
        string repoName,
        long issueNumber,
        CancellationToken ct
    )
    {
        try
        {
            return await storageService.TryGetCachedAnalysisResultAsync(
                owner,
                repoName,
                issueNumber
            );
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
            return null;
        }
    }

    /// <summary>
    /// Fetch the comprehensive issue profile from GitHub service while ensuring repository info is available.
    /// </summary>
    private async Task<IssueProfile?> FetchIssueProfileAsync(
        string owner,
        string repoName,
        long issueNumber,
        CancellationToken ct
    )
    {
        await EnsureRepositoryAvailableAsync(owner, repoName, ct);
        return await gitHubService.BuildComprehensiveIssueProfileAsync(
            owner,
            repoName,
            issueNumber
        );
    }

    /// <summary>
    /// Ensures repository metadata is available (and repo cache directory exists).
    /// </summary>
    private async Task EnsureRepositoryAvailableAsync(
        string owner,
        string repoName,
        CancellationToken ct
    )
    {
        var repoDir = Path.Combine(pathsOptions.Value.IssueRepoDir, owner);
        Directory.CreateDirectory(repoDir);

        // Fire-and-forget the repository info fetch is not desired; await to ensure availability.
        await gitHubClient.GetRepositoryInfoAsync(owner, repoName);
    }

    /// <summary>
    /// Caches user profiles referenced in the issue profile concurrently.
    /// The parallelism is conservative to avoid overwhelming GitHub. It can be made configurable.
    /// </summary>
    private async Task CacheUserProfilesAsync(IssueProfile issueProfile, CancellationToken ct)
    {
        if (issueProfile is null)
            return;

        // Resolve repository context
        var repoFullName = issueProfile.RepositoryFullName ?? string.Empty;
        var parts = repoFullName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            logger.LogWarning(
                "Repository full name '{RepoFullName}' could not be parsed",
                repoFullName
            );
            return;
        }

        var owner = parts[0];
        var repo = parts[1];
        FullRepository repository;
        try
        {
            repository = await gitHubClient.GetRepositoryInfoAsync(owner, repo);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get repository info for {Owner}/{Repo}", owner, repo);
            return;
        }

        // Collect candidate logins (author + commenters + label actors)
        var candidateLogins =
            issueProfile.CommentEvents?.Where(c => c?.By?.Login is not null).Select(c => c.By.Login)
            ?? Enumerable.Empty<string>();

        candidateLogins = candidateLogins.Concat(
            issueProfile.LabelEvents?.Where(l => l?.By?.Login is not null).Select(l => l.By.Login)
                ?? Enumerable.Empty<string>()
        );

        candidateLogins = candidateLogins
            .Append(issueProfile.SdkIssue.User?.Login)
            .Where(login => !string.IsNullOrWhiteSpace(login))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase);

        // Conservative parallelism; change or make configurable as needed
        var parallelism = 6;

        // Use a simple concurrency-safe loop with Parallel.ForEachAsync
        try
        {
            await Parallel.ForEachAsync(
                candidateLogins,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = parallelism,
                    CancellationToken = ct,
                },
                async (login, token) =>
                {
                    try
                    {
                        // The service implements caching internally
                        await gitHubService.GetUserProfileAsync(login, repository);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(
                            ex,
                            "Failed to cache user profile for {Login} in repository {RepoId}",
                            login,
                            repository?.Id
                        );
                    }
                }
            );
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "Caching user profiles cancelled for {Repo}",
                issueProfile.RepositoryFullName
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Unexpected error while caching user profiles for {Repo}",
                issueProfile.RepositoryFullName
            );
        }
    }

    /// <summary>
    /// Sanitize subjective confidences to be within [0,1] and trim very long rationales.
    /// </summary>
    private static void SanitizeSubjectiveConfidences(IssueAnalysisResponse analysis)
    {
        if (analysis?.Subjective is null)
            return;

        analysis.Subjective.ConfidenceInWhetherRealBug = Math.Clamp(
            analysis.Subjective.ConfidenceInWhetherRealBug,
            0.0,
            1.0
        );
        analysis.Subjective.ConfidenceInWhetherDuplicate = Math.Clamp(
            analysis.Subjective.ConfidenceInWhetherDuplicate,
            0.0,
            1.0
        );

        const int maxRationaleLength = 2_000;
        if (
            !string.IsNullOrWhiteSpace(analysis.Subjective.WhetherRealBugRationale)
            && analysis.Subjective.WhetherRealBugRationale.Length > maxRationaleLength
        )
        {
            analysis.Subjective.WhetherRealBugRationale = analysis
                .Subjective
                .WhetherRealBugRationale[..maxRationaleLength];
        }

        if (
            !string.IsNullOrWhiteSpace(analysis.Subjective.WhetherDuplicateRationale)
            && analysis.Subjective.WhetherDuplicateRationale.Length > maxRationaleLength
        )
        {
            analysis.Subjective.WhetherDuplicateRationale = analysis
                .Subjective
                .WhetherDuplicateRationale[..maxRationaleLength];
        }
    }

    /// <summary>
    /// Determine final IssueStatus according to the decision tree:
    /// 1. If no developer judgement -> Inconclusive
    /// 2. If developer explicitly says not a bug -> NotABug
    /// 3. If fixed (including fixed before report) -> Fixed
    /// 4. If duplicate -> Duplicate
    /// 5. Otherwise -> Confirmed
    /// </summary>
    public static IssueStatus DetermineIssueStatus(
        IssueAnalysisResponse analysisResult,
        IssueProfile issueProfile
    )
    {
        // If there are no developer-associated signals, we cannot classify reliably.
        if (analysisResult?.Deterministic?.HasDeveloperJudgement != true)
            return IssueStatus.Inconclusive;

        // Developer explicitly states this is not a bug.
        if (analysisResult.Subjective?.IsRealBug == false)
            return IssueStatus.NotABug;

        // Deterministic evidence that the issue has been fixed (including fixed before report).
        if (analysisResult.Deterministic?.IsFixed == true)
            if (analysisResult.Subjective?.IsRealBug == true)
                return IssueStatus.Fixed;
            else
                Log.Warning(
                    "Issue {IssueNumber} has been fixed, but is not a real bug",
                    issueProfile.SdkIssue.Number
                );

        // Duplicate as indicated by subjective judgement (LLM/developer).
        if (analysisResult.Subjective?.IsDuplicate == true)
            return IssueStatus.Duplicate;

        // Default: developer engagement implies a confirmed issue.
        return IssueStatus.Confirmed;
    }

    /// <summary>
    /// Returns a small tuple for consistent status logging.
    /// </summary>
    private static (string Name, string Description) FormatStatusForLog(IssueStatus status) =>
        (
            status.GetName() ?? status.ToString(),
            status.AsString(EnumFormat.Description) ?? status.ToString()
        );
}
