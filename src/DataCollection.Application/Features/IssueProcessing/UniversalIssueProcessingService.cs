using DataCollection.Application.Features.IssueAnalysis.Rules;
using DataCollection.Core.Models.IssueTracker;
using DataCollection.Core.Models.IssueTracker.Responses;
using DataCollection.Infrastructure.Clients.IssueTrackers;
using Microsoft.Extensions.Logging;

namespace DataCollection.Application.Features.IssueProcessing;

public class UniversalIssueProcessingService(
    ILogger<UniversalIssueProcessingService> logger,
    IIssueTrackerClientFactory clientFactory,
    UniversalIssueOverallStatusCriterion statusCriterion
)
{
    /// <summary>
    /// Processes an issue from any supported issue tracker
    /// </summary>
    public async Task<(IssueStatus Status, IssueAnalysisResponse Analysis)?> ProcessIssueAsync(
        string url,
        bool saveResults = false
    )
    {
        try
        {
            // Detect provider and extract details
            var provider = clientFactory.DetectProviderFromUrl(url);
            if (provider == BugTrackingProvider.Unknown || provider == null)
            {
                logger.LogError("Unsupported or invalid issue tracker URL: {Url}", url);
                return null;
            }

            var repositoryIdentifier = clientFactory.ExtractRepositoryIdentifier(
                url,
                provider.Value
            );
            var issueId = clientFactory.ExtractIssueId(url, provider.Value);

            if (string.IsNullOrEmpty(repositoryIdentifier) || string.IsNullOrEmpty(issueId))
            {
                logger.LogError("Could not extract repository or issue ID from URL: {Url}", url);
                return null;
            }

            logger.LogInformation(
                "Processing issue: {Provider} {RepositoryId}#{IssueId}",
                provider.Value,
                repositoryIdentifier,
                issueId
            );

            // Build comprehensive issue profile
            var universalProfile = await BuildComprehensiveIssueProfileAsync(
                provider.Value,
                repositoryIdentifier,
                issueId
            );

            if (universalProfile == null)
            {
                logger.LogError("Failed to build issue profile for {Url}", url);
                return null;
            }

            // Analyze the issue
            var analysisResult = await statusCriterion.EvaluateAsync(universalProfile);

            logger.LogDebug("Analysis: {AnalysisResult}", analysisResult);

            // Determine final status
            var currentStatus = DetermineIssueStatus(analysisResult, universalProfile);

            logger.LogInformation(
                "Issue status decision complete for {Provider} {RepositoryId}#{IssueId}. Status: {Status}. Explanation: {Explanation}",
                provider.Value,
                repositoryIdentifier,
                issueId,
                currentStatus,
                analysisResult.NuanceOrExplanation
            );

            // TODO: Save results if requested
            if (saveResults)
            {
                logger.LogInformation("Result saving not yet implemented for universal profiles");
            }

            return (currentStatus, analysisResult);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing issue from URL: {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// Builds a comprehensive issue profile for any supported issue tracker
    /// </summary>
    private async Task<UniversalIssueProfile?> BuildComprehensiveIssueProfileAsync(
        BugTrackingProvider provider,
        string repositoryIdentifier,
        string issueId
    )
    {
        try
        {
            var client = clientFactory.CreateClient(provider);

            // Get repository information
            var repository = await client.GetRepositoryAsync(repositoryIdentifier);
            if (repository == null)
            {
                logger.LogWarning("Repository not found: {RepositoryId}", repositoryIdentifier);
                // Create a minimal repository for analysis
                repository = new Repository
                {
                    Identifier = repositoryIdentifier,
                    Name = repositoryIdentifier.Split('/').LastOrDefault() ?? repositoryIdentifier,
                    Provider = provider,
                };
            }

            // Get issue information
            var issue = await client.GetIssueAsync(repositoryIdentifier, issueId);
            if (issue == null)
            {
                logger.LogError(
                    "Issue not found: {RepositoryId}#{IssueId}",
                    repositoryIdentifier,
                    issueId
                );
                return null;
            }

            // Get comments and events
            var comments = await client.GetIssueCommentsAsync(repositoryIdentifier, issueId);
            var events = await client.GetIssueEventsAsync(repositoryIdentifier, issueId);

            // Get author profile
            var authorProfile = await client.GetUserProfileAsync(issue.Author);
            if (authorProfile == null)
            {
                logger.LogWarning("Author profile not found for: {Author}", issue.Author);
                authorProfile = new UniversalUserProfile
                {
                    Username = issue.Author,
                    Provider = provider,
                    ActivitySummary = "Fallback profile - no metrics",
                    ContributionMetrics = null,
                    ProjectInvolvement = null,
                    ActivityLevel = "Unknown",
                };
            }

            // Get profiles for all participants (commenters, assignees, etc.)
            var participantNames = new HashSet<string> { issue.Author };
            participantNames.UnionWith(comments.Select(c => c.Author));
            participantNames.UnionWith(events.Select(e => e.Actor));
            participantNames.UnionWith(issue.Assignees);

            var participants = new List<UniversalUserProfile> { authorProfile };
            foreach (var participantName in participantNames.Where(p => p != issue.Author))
            {
                var profile = await client.GetUserProfileAsync(participantName);
                if (profile != null)
                {
                    participants.Add(profile);
                }
                else
                {
                    // Create minimal profile for unknown users
                    participants.Add(
                        new UniversalUserProfile
                        {
                            Username = participantName,
                            Provider = provider,
                            ActivitySummary = "Minimal participant profile",
                            ContributionMetrics = null,
                            ProjectInvolvement = null,
                            ActivityLevel = "Unknown",
                        }
                    );
                }
            }

            // Calculate metrics
            var timeToFirstResponse = CalculateTimeToFirstResponse(issue, comments);
            var timeToResolution = CalculateTimeToResolution(issue);

            return new UniversalIssueProfile
            {
                CoreIssue = issue,
                CoreRepository = repository,
                AuthorProfile = authorProfile,
                Comments = comments,
                Events = events,
                Participants = participants,
                TimeToFirstResponse = timeToFirstResponse,
                TimeToResolution = timeToResolution,
            };
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error building comprehensive issue profile for {Provider} {RepositoryId}#{IssueId}",
                provider,
                repositoryIdentifier,
                issueId
            );
            return null;
        }
    }

    private static TimeSpan? CalculateTimeToFirstResponse(Issue issue, List<Comment> comments)
    {
        var firstComment = comments
            .Where(c => c.Author != issue.Author)
            .OrderBy(c => c.CreatedAt)
            .FirstOrDefault();

        return firstComment != null ? firstComment.CreatedAt - issue.CreatedAt : null;
    }

    private static TimeSpan? CalculateTimeToResolution(Issue issue)
    {
        return issue.ClosedAt.HasValue ? issue.ClosedAt.Value - issue.CreatedAt : null;
    }

    private static IssueStatus DetermineIssueStatus(
        IssueAnalysisResponse analysis,
        UniversalIssueProfile profile
    )
    {
        // Use the same logic as GitHub-specific version
        if (analysis.IsRealBug == false)
            return IssueStatus.NotABug;

        if (analysis.IsFixed == true)
            return analysis.IsFixedBeforeIssueRaised == true
                ? IssueStatus.FixedBeforeReport
                : IssueStatus.ConfirmedFixed;

        if (analysis.IsBugButWontFix == true)
            return IssueStatus.ConfirmedWontFix;

        if (analysis.IsDuplicate == true)
            return IssueStatus.Duplicate;

        if (analysis.IsRealBug == true)
        {
            return profile.CoreIssue.Status == Core.Models.IssueTracker.IssueStatus.Open
                ? IssueStatus.ConfirmedWaitingForAction
                : IssueStatus.Unknown;
        }

        return IssueStatus.Unknown;
    }
}
