using ConsoleAppFramework;
using DataCollection.Application.Features.IssueProcessing;
using DataCollection.Core.Models.IssueTracker;
using DataCollection.Infrastructure.Clients.IssueTrackers;
using DataCollection.Presentation.Cli.Filters;
using Microsoft.Extensions.Logging;

namespace DataCollection.Presentation.Cli.Commands;

[RegisterCommands("issue")]
[ConsoleAppFilter<PathsOptionsFilter>]
[ConsoleAppFilter<CredentialOptionsFilter>]
[ConsoleAppFilter<EnsureDBFilter>]
public class IssueCommands(
    ILogger<IssueCommands> logger,
    SingleIssueProcessingService singleIssueService,
    IssueBatchProcessingService batchProcessingService,
    UniversalIssueProcessingService universalIssueService,
    IIssueTrackerClientFactory issueTrackerFactory
)
{
    /// <summary>
    /// Analyzes an issue and determines its status. Supports GitHub, Bugzilla, and Jira.
    /// </summary>
    /// <param name="url">Optional URL to the issue (GitHub, Bugzilla, or Jira)</param>
    /// <param name="owner">The owner of the repository (if URL not provided, GitHub only)</param>
    /// <param name="repoName">The name of the repository (if URL not provided, GitHub only)</param>
    /// <param name="issueNumber">The number of the issue (if URL not provided, GitHub only)</param>
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
        // Parse URL if provided
        if (url != null)
        {
            var provider = issueTrackerFactory.DetectProviderFromUrl(url);
            if (provider == null || provider == BugTrackingProvider.Unknown)
            {
                logger.LogError("Unsupported or invalid issue tracker URL: {Url}", url);
                return;
            }

            // For non-GitHub providers, use universal processing service
            if (provider != BugTrackingProvider.GitHub)
            {
                logger.LogInformation(
                    "Processing non-GitHub issue with universal service: {Url}",
                    url
                );
                var result = await universalIssueService.ProcessIssueAsync(url, saveResults);

                if (result.HasValue)
                {
                    logger.LogInformation(
                        "✅ Issue analysis complete! Status: {Status}, Explanation: {Explanation}",
                        result.Value.Status,
                        result.Value.Analysis.NuanceOrExplanation
                    );
                }
                else
                {
                    logger.LogError("❌ Failed to process issue");
                }
                return;
            }

            // GitHub-specific parsing for backward compatibility
            var (parsedOwner, parsedRepo, parsedIssueNumber) = ParseGitHubIssueUrl(url);
            if (parsedOwner == null || parsedRepo == null || !parsedIssueNumber.HasValue)
            {
                logger.LogError("Invalid GitHub issue URL format: {Url}", url);
                return;
            }
            owner = parsedOwner;
            repoName = parsedRepo;
            issueNumber = parsedIssueNumber;
        }

        // Validate required parameters
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

        logger.LogInformation(
            "Deciding status for issue: {Owner}/{Repo}#{IssueNumber}",
            owner,
            repoName,
            issueNumber.Value
        );

        try
        {
            await singleIssueService.ProcessIssueAsync(
                owner,
                repoName,
                issueNumber.Value,
                useCache: useCache,
                saveResults: saveResults
            );
        }
        catch (Exception apiEx)
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
    /// Process a batch of issues from a file using OpenAI Batch API
    /// </summary>
    /// <param name="inputFile">Path to a file containing issue URLs or owner/repo/issue combinations, one per line</param>
    /// <param name="saveResults">Whether to save analysis results to disk</param>
    /// <param name="useCache">Whether to use cached results if available</param>
    /// <param name="useBatchApi">Whether to use OpenAI Batch API for processing (default true for cost savings)</param>
    /// <param name="batchJobId">Optional existing OpenAI batch job ID to resume/check status instead of creating new batch</param>
    public async Task ProcessBatch(
        string inputFile,
        bool saveResults = true,
        bool useCache = true,
        bool useBatchApi = true,
        string? batchJobId = null
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

        // Parse and collect all valid issues
        var issueTasks = ParseIssueLines(lines);

        logger.LogInformation(
            "Found {ValidCount} valid issues out of {TotalCount}",
            issueTasks.Count,
            lines.Length
        );

        // De-duplicate issues
        issueTasks = [.. issueTasks.Distinct()];

        // Delegate to appropriate batch processing service
        if (useBatchApi)
            await batchProcessingService.ProcessBatchWithOpenAIAsync(
                issueTasks,
                saveResults,
                useCache,
                batchJobId
            );
        else
        {
            if (!string.IsNullOrEmpty(batchJobId))
                logger.LogWarning(
                    "Batch job ID provided but useBatchApi is false. Ignoring batch job ID and using parallel processing."
                );

            await batchProcessingService.ProcessBatchWithParallelismAsync(
                issueTasks,
                saveResults,
                useCache
            );
        }
    }

    /// <summary>
    /// Demonstrates the new issue tracker functionality with Bugzilla and Jira
    /// </summary>
    private async Task DemonstrateIssueTrackerAsync(string url, BugTrackingProvider provider)
    {
        logger.LogInformation(
            "🚀 Demonstrating {Provider} issue tracker support with URL: {Url}",
            provider,
            url
        );

        try
        {
            var client = issueTrackerFactory.CreateClient(provider);
            logger.LogInformation("✅ Created {Provider} client successfully", provider);

            var repositoryId = issueTrackerFactory.ExtractRepositoryIdentifier(url, provider);
            var issueId = issueTrackerFactory.ExtractIssueId(url, provider);

            logger.LogInformation("📋 Extracted Repository ID: {RepositoryId}", repositoryId);
            logger.LogInformation("🔍 Extracted Issue ID: {IssueId}", issueId);

            if (repositoryId != null && issueId != null)
            {
                // Try to get repository info
                var repository = await client.GetRepositoryAsync(repositoryId);
                if (repository != null)
                {
                    logger.LogInformation(
                        "✅ Repository: {Name} - {Description}",
                        repository.Name,
                        repository.Description
                    );
                }

                // Try to get issue info
                var issue = await client.GetIssueAsync(repositoryId, issueId);
                if (issue != null)
                {
                    logger.LogInformation("✅ Issue: {Title}", issue.Title);
                    logger.LogInformation("   📝 Status: {Status}", issue.Status);
                    logger.LogInformation("   👤 Author: {Author}", issue.Author);
                    logger.LogInformation(
                        "   🔧 Assignees: {Assignees}",
                        string.Join(", ", issue.Assignees)
                    );
                    logger.LogInformation(
                        "   🏷️ Labels: {Labels}",
                        string.Join(", ", issue.Labels)
                    );
                    logger.LogInformation("   📅 Created: {Created}", issue.CreatedAt);

                    if (issue.UpdatedAt.HasValue)
                        logger.LogInformation("   📅 Updated: {Updated}", issue.UpdatedAt);

                    // Try to get comments
                    var comments = await client.GetIssueCommentsAsync(repositoryId, issueId);
                    logger.LogInformation("   💬 Comments: {Count}", comments.Count);

                    // Try to get events
                    var events = await client.GetIssueEventsAsync(repositoryId, issueId);
                    logger.LogInformation("   📊 Events: {Count}", events.Count);

                    // Try to get user profile for author
                    var userProfile = await client.GetUserProfileAsync(issue.Author);
                    if (userProfile != null)
                    {
                        logger.LogInformation("   👤 Author Profile:");
                        logger.LogInformation(
                            "      🔰 Is Developer: {IsDeveloper}",
                            userProfile.IsDeveloper
                        );
                        logger.LogInformation(
                            "      🔧 Is Maintainer: {IsMaintainer}",
                            userProfile.IsMaintainer
                        );
                        logger.LogInformation(
                            "      ⚡ Is Committer: {IsCommitter}",
                            userProfile.IsCommitter
                        );
                        logger.LogInformation(
                            "      🏷️ Role Indicators: {RoleIndicators}",
                            userProfile.RoleIndicators
                        );
                        logger.LogInformation(
                            "      📊 Activity Level: {ActivityLevel}",
                            userProfile.ActivityLevel ?? "Unknown"
                        );
                        if (!string.IsNullOrEmpty(userProfile.ActivitySummary))
                        {
                            logger.LogInformation(
                                "      📈 Activity: {ActivitySummary}",
                                userProfile.ActivitySummary
                            );
                        }
                    }
                }
                else
                {
                    logger.LogWarning(
                        "⚠️ Issue not found or not accessible (may require authentication)"
                    );
                }
            }
            else
            {
                logger.LogError("❌ Could not extract repository or issue information from URL");
            }

            logger.LogInformation("🎉 {Provider} demonstration complete!", provider);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "❌ Error demonstrating {Provider} support: {Message}",
                provider,
                ex.Message
            );
        }
    }

    /// <summary>
    /// Parses GitHub issue URL and extracts owner, repo, and issue number
    /// </summary>
    private (string? Owner, string? Repo, long? IssueNumber) ParseGitHubIssueUrl(string url)
    {
        var isUri = Uri.TryCreate(url, UriKind.Absolute, out var uri);
        if (!isUri || uri == null)
            return (null, null, null);

        if (uri.Segments.Length < 5)
        {
            logger.LogError(
                "Invalid GitHub issue URL format: {Url}. Expected at least 5 segments.",
                url
            );
            return (null, null, null);
        }

        var owner = uri.Segments[1].TrimEnd('/');
        var repo = uri.Segments[2].TrimEnd('/');
        var shouldBeIssueNumber = uri.Segments[4].TrimEnd('/');

        if (!long.TryParse(shouldBeIssueNumber, out var issueNumber))
        {
            logger.LogError(
                "Could not parse issue number from URL segment: {Segment}",
                shouldBeIssueNumber
            );
            return (null, null, null);
        }

        return (owner, repo, issueNumber);
    }

    /// <summary>
    /// Parses lines from input file and extracts issue information
    /// </summary>
    private List<(string? Url, string? Owner, string? Repo, long? Number)> ParseIssueLines(
        string[] lines
    )
    {
        var issueTasks = new List<(string? Url, string? Owner, string? Repo, long? Number)>();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith('#'))
                continue;

            if (trimmedLine.StartsWith("http"))
                // Process as URL
                issueTasks.Add((trimmedLine, null, null, null));
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

        return issueTasks;
    }
}
