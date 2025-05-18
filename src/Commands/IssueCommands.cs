using System;
using System.Threading.Tasks;
using ConsoleAppFramework;
using DataCollection.Models.IssueTracker;
using DataCollection.Options;
using DataCollection.Services;
using EnumsNET;
using Microsoft.Extensions.Logging;
using Octokit;

namespace DataCollection.Commands;

[RegisterCommands("issue")]
[ConsoleAppFilter<CredentialOptions.Filter>]
public class IssueCommands(
    ILogger<IssueCommands> logger,
    GitHubService gitHubService,
    IssueOverallStatusCriterion statusCriterion
)
{
    /// <summary>
    /// Analyzes an issue and determines its status.
    /// </summary>
    /// <param name="url">Optional URL to the GitHub issue</param>
    /// <param name="owner">The owner of the repository (if URL not provided)</param>
    /// <param name="repoName">The name of the repository (if URL not provided)</param>
    /// <param name="issueNumber">The number of the issue (if URL not provided)</param>
    public async Task DecideStatus(
        string? url,
        string? owner = null,
        string? repoName = null,
        long? issueNumber = null
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

        logger.LogInformation(
            "Deciding status for issue: {Owner}/{Repo}#{IssueNumber}",
            owner,
            repoName,
            issueNumber.Value
        );

        try
        {
            await DecideStatus(owner, repoName, issueNumber.Value);
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

    private async Task DecideStatus(string owner, string repoName, long issueNumber)
    {
        var issueProfile = await gitHubService.BuildComprehensiveIssueProfileAsync(
            owner,
            repoName,
            issueNumber
        );
        var analysisResult = await statusCriterion.EvaluateAsync(issueProfile);

        logger.LogDebug("Analysis: {AnalysisResult}", analysisResult);

        IssueStatus currentStatus;

        if (analysisResult.IsDuplicate)
            currentStatus = IssueStatus.Duplicate;
        else if (analysisResult.IsFixedBeforeIssueRaised)
            currentStatus = IssueStatus.FixedBeforeReport;
        else if (!analysisResult.IsRealBug)
            currentStatus = IssueStatus.NotABug;
        else if (issueProfile.IsClosed)
            if (analysisResult.IsFixed)
                currentStatus = IssueStatus.ConfirmedFixed;
            else
                currentStatus = analysisResult.WontFix
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
            analysisResult.Explanation
        );
    }
}
