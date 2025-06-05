using DataCollection.Application.Features.BugDiscovery;
using DataCollection.Application.Features.IssueAnalysis.Rules;
using DataCollection.Application.Models.IssueTracker.Profiles;
using DataCollection.Core.Models.IssueTracker;
using DataCollection.Core.Models.IssueTracker.Responses;
using DataCollection.Core.Options;
using EnumsNET;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Application.Features.IssueProcessing;

public class SingleIssueProcessingService(
    ILogger<SingleIssueProcessingService> logger,
    GitHubService gitHubService,
    IssueOverallStatusCriterion statusCriterion,
    DatabaseIssueAnalysisStorageService storageService,
    IOptions<PathsOptions> pathsOptions
)
{
    private readonly PathsOptions _pathsOptions = pathsOptions.Value;

    /// <summary>
    /// Processes a single issue and returns the status and analysis result
    /// </summary>
    public async Task<(IssueStatus Status, IssueAnalysisResponse Analysis)> ProcessIssueAsync(
        string owner,
        string repoName,
        long issueNumber,
        bool useCache = true,
        bool saveResults = false
    )
    {
        // Check cache first if enabled
        if (useCache)
        {
            var cachedResult = await storageService.TryGetCachedAnalysisResultAsync(
                owner,
                repoName,
                issueNumber
            );
            if (cachedResult != null)
            {
                logger.LogInformation(
                    "{Owner}/{Repo}#{IssueNumber} is cached. Status: {Status} {StatusDescription} {StatusExplanation}",
                    owner,
                    repoName,
                    issueNumber,
                    cachedResult.Status.GetName(),
                    cachedResult.Status.AsString(EnumFormat.Description),
                    cachedResult.Analysis.NuanceOrExplanation
                );
                return (cachedResult.Status, cachedResult.Analysis);
            }
        }

        logger.LogInformation(
            "Processing issue: {Owner}/{Repo}#{IssueNumber}",
            owner,
            repoName,
            issueNumber
        );

        // Ensure repository is cached
        await EnsureRepoCachedAsync(owner, repoName);

        // Build comprehensive issue profile
        var issueProfile = await gitHubService.BuildComprehensiveIssueProfileAsync(
            owner,
            repoName,
            issueNumber
        );

        // Cache user profiles
        await CacheUserProfilesAsync(issueProfile);

        // Analyze the issue
        var analysisResult = await statusCriterion.EvaluateAsync(issueProfile);

        logger.LogDebug("Analysis: {AnalysisResult}", analysisResult);

        // Determine final status
        var currentStatus = DetermineIssueStatus(analysisResult, issueProfile);

        logger.LogInformation(
            "Issue status decision process complete for {Owner}/{Repo}#{IssueNumber}. Concluded {StatusName} {StatusMessage}. LLM Explanation: {Explanation}",
            owner,
            repoName,
            issueNumber,
            currentStatus.GetName(),
            currentStatus.AsString(EnumFormat.Description),
            analysisResult.NuanceOrExplanation
        );

        // Save results if requested
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
    /// Determines the issue status based on analysis result and issue profile
    /// </summary>
    public static IssueStatus DetermineIssueStatus(
        IssueAnalysisResponse analysisResult,
        IssueProfile issueProfile
    )
    {
        if (analysisResult.IsDuplicate == true)
            return IssueStatus.Duplicate;
        else if (analysisResult.IsFixedBeforeIssueRaised == true)
            return IssueStatus.FixedBeforeReport;
        else if (analysisResult.IsRealBug == false)
            return IssueStatus.NotABug;
        else if (issueProfile.IsClosed)
            if (analysisResult.IsFixed == true)
                return IssueStatus.ConfirmedFixed;
            else
                return analysisResult.IsBugButWontFix == true
                    ? IssueStatus.ConfirmedWontFix
                    : IssueStatus.Pending;
        else
            return IssueStatus.ConfirmedWaitingForAction;
    }

    /// <summary>
    /// Ensures the repository is cached
    /// </summary>
    private async Task EnsureRepoCachedAsync(string owner, string repoName)
    {
        var repoDir = Path.Combine(_pathsOptions.IssueRepoDir, owner);
        Directory.CreateDirectory(repoDir);

        await gitHubService.GetRepositoryInfoAsync(owner, repoName);
    }

    /// <summary>
    /// Caches user profiles from the issue
    /// </summary>
    private async Task CacheUserProfilesAsync(IssueProfile issueProfile)
    {
        // Extract repository info from issueProfile
        var repoFullName = issueProfile.RepositoryFullName;
        var parts = repoFullName.Split('/');
        var owner = parts[0];
        var repoName = parts[1];
        var repository = await gitHubService.GetRepositoryInfoAsync(owner, repoName);

        var userLogins = new HashSet<string> { issueProfile.SdkIssue.User?.Login ?? "unknown" };

        foreach (var comment in issueProfile.CommentEvents)
            userLogins.Add(comment.By.Login);

        foreach (var label in issueProfile.LabelEvents)
            userLogins.Add(label.By.Login);

        foreach (var login in userLogins)
        {
            try
            {
                // For simplicity, pass null as the user object since we only have login
                // The service will fetch the user details internally
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
