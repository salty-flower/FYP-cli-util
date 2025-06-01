using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using DataCollection.Models.GitHub;
using GitHub.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace DataCollection.Services;

/// <summary>
/// Service for manual GitHub API calls to work around SDK limitations/bugs
/// </summary>
public class GitHubManualApiService
{
    private readonly HttpClient httpClient;
    private readonly ILogger<GitHubManualApiService> logger;
    private readonly JsonSerializerOptions jsonOptions;

    public GitHubManualApiService(
        IHttpClientFactory httpClientFactory,
        ILogger<GitHubManualApiService> logger
    )
    {
        this.httpClient = httpClientFactory.CreateClient("github-api");
        this.logger = logger;
        this.jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    /// <summary>
    /// Creates a clean JSON description for event data, excluding null values
    /// </summary>
    public static string CreateEventDescription(GitHubEvent evt)
    {
        return JsonConvert.SerializeObject(
            new
            {
                evt.Rename,
                evt.RequestedReviewer,
                evt.ReviewRequester,
                evt.Assigner,
                evt.Assignee,
                evt.DismissedReview,
                evt.Milestone,
            },
            new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.None,
            }
        );
    }

    /// <summary>
    /// Fetch issue events manually to avoid SDK integer overflow bug
    /// https://github.com/octokit/dotnet-sdk/issues/117
    /// </summary>
    public async Task<List<GitHubEvent>> GetIssueEventsAsync(
        string owner,
        string repoName,
        long issueNumber
    )
    {
        try
        {
            logger.LogDebug(
                "Fetching issue events for {Owner}/{Repo}#{IssueNumber}",
                owner,
                repoName,
                issueNumber
            );

            var response = await httpClient.GetAsync(
                $"repos/{owner}/{repoName}/issues/{issueNumber}/events"
            );
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var events = JsonSerializer.Deserialize<List<GitHubEvent>>(json, jsonOptions);

            logger.LogDebug(
                "Successfully fetched {Count} events for {Owner}/{Repo}#{IssueNumber}",
                events?.Count ?? 0,
                owner,
                repoName,
                issueNumber
            );

            return events ?? [];
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("401"))
        {
            logger.LogWarning(
                "Unauthorized access to GitHub API for {Owner}/{Repo}#{IssueNumber}. Check your token permissions.",
                owner,
                repoName,
                issueNumber
            );
            return [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to fetch issue events for {Owner}/{Repo}#{IssueNumber}",
                owner,
                repoName,
                issueNumber
            );
            return [];
        }
    }

    /// <summary>
    /// Fetch issue comments manually to avoid SDK integer overflow bug
    /// </summary>
    public async Task<List<IssueComment>> GetIssueCommentsAsync(
        string owner,
        string repoName,
        long issueNumber
    )
    {
        try
        {
            logger.LogDebug(
                "Fetching issue comments for {Owner}/{Repo}#{IssueNumber}",
                owner,
                repoName,
                issueNumber
            );

            var response = await httpClient.GetAsync(
                $"repos/{owner}/{repoName}/issues/{issueNumber}/comments"
            );
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var comments = JsonSerializer.Deserialize<List<IssueComment>>(json, jsonOptions);

            logger.LogDebug(
                "Successfully fetched {Count} comments for {Owner}/{Repo}#{IssueNumber}",
                comments?.Count ?? 0,
                owner,
                repoName,
                issueNumber
            );

            return comments ?? [];
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("401"))
        {
            logger.LogWarning(
                "Unauthorized access to GitHub API for {Owner}/{Repo}#{IssueNumber}. Check your token permissions.",
                owner,
                repoName,
                issueNumber
            );
            return [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to fetch issue comments for {Owner}/{Repo}#{IssueNumber}",
                owner,
                repoName,
                issueNumber
            );
            return [];
        }
    }
}
