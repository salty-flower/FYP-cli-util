using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using DataCollection.Infrastructure.Models.GitHub;
using DataCollection.Infrastructure.Serialization;
using GitHub.Models;
using Microsoft.Extensions.Logging;
using Refit;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

public class GitHubClient(
    GitHub.GitHubClient gitHubClient,
    IGitHubApi gitHubApi,
    IRepositoryCache repositoryCache,
    IIssueCommentsCache issueCommentsCache,
    IIssueEventsCache issueEventsCache,
    ISearchResultsCache searchResultsCache,
    ILogger<GitHubClient> logger,
    IHttpClientFactory httpClientFactory
) : IGitHubClient
{
    private readonly ConcurrentDictionary<long, IReadOnlyList<Contributor>> repoContributorsCache =
    [];
    private readonly ConcurrentDictionary<string, object> userCache = [];

    public async Task<List<string>> SearchForRepositoryAsync(string keyword) =>
        (
            await gitHubClient.Search.Repositories.GetAsync(requestConfiguration =>
                requestConfiguration.QueryParameters.Q = keyword
            )
        )
            ?.Items?.Select(repo => repo.FullName)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Cast<string>()
            .ToList() ?? [];

    public async Task<FullRepository> GetRepositoryInfoAsync(
        string owner,
        string repoName,
        CancellationToken cancellationToken = default
    )
    {
        var cachedRepo = await repositoryCache.TryGetAsync(owner, repoName);
        if (cachedRepo != null)
        {
            return cachedRepo;
        }

        var repository =
            await gitHubClient.Repos[owner][repoName].GetAsync(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException($"Repository {owner}/{repoName} not found");

        await repositoryCache.SetAsync(owner, repoName, repository);

        return repository;
    }

    public async Task<object> GetUserAsync(string userLogin)
    {
        if (userCache.TryGetValue(userLogin, out var cachedUser))
            return cachedUser;

        var user = await gitHubClient.Users[userLogin].GetAsync();

        userCache[userLogin] =
            user ?? throw new InvalidOperationException($"User {userLogin} not found");
        return user;
    }

    public async Task<bool> IsUserContributorAsync(string userLogin, FullRepository repository)
    {
        var repoId = repository.Id.GetValueOrDefault();
        if (repoContributorsCache.TryGetValue(repoId, out var contributors))
            return contributors.Any(c => c.Login == userLogin);

        // This is the most reliable public method to check for contributor status
        // as the /collaborators endpoint requires admin/write/maintain permissions.
        // This call can be expensive for repos with many contributors, but is cached after the first call.
        logger.LogInformation(
            "Fetching full contributor list for {Owner}/{RepoName} to check contributor status",
            repository.Owner?.Login,
            repository.Name
        );

        var contributorsResponse = await gitHubClient
            .Repos[
                repository.Owner?.Login
                    ?? throw new InvalidOperationException("Repository owner is null")
            ][repository.Name ?? throw new InvalidOperationException("Repository name is null")]
            .Contributors.GetAsync();
        contributors = contributorsResponse ?? new List<Contributor>();

        repoContributorsCache[repoId] = contributors.ToList().AsReadOnly();

        return contributors.Any(c => c.Login == userLogin);
    }

    public async Task<int> GetUserIssuesCountAsync(string userLogin, FullRepository repository)
    {
        var searchQuery =
            $"type:issue author:{userLogin} repo:{repository.Owner?.Login}/{repository.Name}";

        var cachedCount = await searchResultsCache.TryGetAsync(searchQuery);
        if (cachedCount.HasValue)
        {
            return cachedCount.Value;
        }

        try
        {
            var searchJson = await gitHubApi.SearchIssuesAsync(searchQuery);
            var searchResponse = JsonSerializer.Deserialize<GitHubSearchResponse>(
                searchJson,
                GitHubAPIJsonContext.Default.GitHubSearchResponse
            );
            var count = searchResponse?.TotalCount ?? 0;
            await searchResultsCache.SetAsync(searchQuery, count);
            return count;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            logger.LogWarning(
                "GitHub search validation failed for query '{Query}': {Message}. User '{User}' may not exist or be searchable.",
                searchQuery,
                ex.Content,
                userLogin
            );
            await searchResultsCache.SetAsync(searchQuery, 0);
            return 0;
        }
    }

    public async Task<(int total, int merged)> GetUserPullRequestsAsync(
        string userLogin,
        FullRepository repository
    )
    {
        var baseQuery =
            $"type:pr author:{userLogin} repo:{repository.Owner?.Login}/{repository.Name}";
        var totalCountTask = GetSearchCountAsync(baseQuery);
        var mergedCountTask = GetSearchCountAsync($"{baseQuery} is:merged");

        await Task.WhenAll(totalCountTask, mergedCountTask);

        return (totalCountTask.Result, mergedCountTask.Result);
    }

    public async Task<GitHubIssue?> GetIssueWithLabelsAsync(
        string owner,
        string repoName,
        long issueNumber,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await gitHubApi.GetIssueAsync(owner, repoName, issueNumber, cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning(
                "Issue {IssueNumber} not found at {Owner}/{RepoName}: {StatusCode} {Message}",
                issueNumber,
                owner,
                repoName,
                ex.StatusCode,
                ex.Content
            );
            return null;
        }
        catch (ApiException ex)
        {
            logger.LogWarning(
                ex,
                "Failed to get issue {IssueNumber} at {Owner}/{RepoName}: {StatusCode} {Message}",
                issueNumber,
                owner,
                repoName,
                ex.StatusCode,
                ex.Content
            );
            return null;
        }
    }

    public async Task<Issue?> GetIssueAsync(string owner, string repoName, long issueNumber)
    {
        try
        {
            // Get repository info to get the repository ID for the API call
            var repository = await GetRepositoryInfoAsync(
                owner,
                repoName,
                cancellationToken: default
            );
            var repositoryId = repository.Id.GetValueOrDefault();

            if (repositoryId == 0)
            {
                logger.LogWarning("Repository {Owner}/{RepoName} has invalid ID", owner, repoName);
                return null;
            }

            // Use our working Refit client instead of the failing GitHub SDK
            return await gitHubApi.GetIssueByRepositoryIdAsync(repositoryId, issueNumber);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Issue {IssueNumber} not found at {Owner}/{RepoName}: {Message}",
                issueNumber,
                owner,
                repoName,
                ex.Message
            );
            return null;
        }
    }

    public async Task<List<GitHubIssueComment>?> GetIssueCommentsAsync(
        string owner,
        string repoName,
        long issueNumber,
        CancellationToken cancellationToken = default
    )
    {
        var cachedComments = await issueCommentsCache.TryGetAsync(owner, repoName, issueNumber);
        if (cachedComments != null)
        {
            return cachedComments;
        }

        // Manual HTTP GET and parse
        var client = httpClientFactory.CreateClient("github-api");
        var requestUri = $"repos/{owner}/{repoName}/issues/{issueNumber}/comments";
        try
        {
            using var response = await client.GetAsync(requestUri, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // Try redirected location via issue JSON
                var redirectedLocation = await GetRedirectedIssueLocationAsync(
                    owner,
                    repoName,
                    issueNumber
                );
                if (redirectedLocation.HasValue)
                {
                    var (newOwner, newRepoName, newIssueNumber) = redirectedLocation.Value;
                    var altUri = $"repos/{newOwner}/{newRepoName}/issues/{newIssueNumber}/comments";
                    using var altResp = await client.GetAsync(altUri, cancellationToken);
                    if (!altResp.IsSuccessStatusCode)
                    {
                        logger.LogWarning(
                            "Failed to get comments from redirected location {Uri}: {Status}",
                            altUri,
                            altResp.StatusCode
                        );
                        await issueCommentsCache.SetAsync(owner, repoName, issueNumber, null);
                        return null;
                    }
                    var altJson = await altResp.Content.ReadAsStringAsync(cancellationToken);
                    var altComments = System.Text.Json.JsonSerializer.Deserialize<
                        List<GitHubIssueComment>
                    >(altJson, GitHubAPIJsonContext.Default.GitHubIssueComment.ListTypeInfo);
                    await issueCommentsCache.SetAsync(owner, repoName, issueNumber, altComments);
                    return altComments;
                }

                await issueCommentsCache.SetAsync(owner, repoName, issueNumber, null);
                return null;
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var comments = System.Text.Json.JsonSerializer.Deserialize<List<GitHubIssueComment>>(
                json,
                GitHubAPIJsonContext.Default.GitHubIssueComment.ListTypeInfo
            );
            await issueCommentsCache.SetAsync(owner, repoName, issueNumber, comments);
            return comments;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to get issue comments for {Owner}/{RepoName}#{IssueNumber}",
                owner,
                repoName,
                issueNumber
            );
            await issueCommentsCache.SetAsync(owner, repoName, issueNumber, null);
            return null;
        }
    }

    public async Task<List<GitHubEvent>?> GetIssueEventsAsync(
        string owner,
        string repoName,
        long issueNumber,
        CancellationToken cancellationToken = default
    )
    {
        var cachedEvents = await issueEventsCache.TryGetAsync(owner, repoName, issueNumber);
        if (cachedEvents != null)
        {
            return cachedEvents;
        }

        try
        {
            var events = await gitHubApi.GetIssueEventsAsync(
                owner,
                repoName,
                issueNumber,
                cancellationToken
            );
            await issueEventsCache.SetAsync(owner, repoName, issueNumber, events);
            return events;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogInformation(
                "Issue events not found at {Owner}/{RepoName}#{IssueNumber}, checking for redirected location",
                owner,
                repoName,
                issueNumber
            );

            var redirectedLocation = await GetRedirectedIssueLocationAsync(
                owner,
                repoName,
                issueNumber
            );
            if (redirectedLocation.HasValue)
            {
                var (newOwner, newRepoName, newIssueNumber) = redirectedLocation.Value;
                logger.LogInformation(
                    "Found redirected issue location: {NewOwner}/{NewRepoName}#{NewIssueNumber}",
                    newOwner,
                    newRepoName,
                    newIssueNumber
                );

                try
                {
                    var events = await gitHubApi.GetIssueEventsAsync(
                        newOwner,
                        newRepoName,
                        newIssueNumber,
                        cancellationToken
                    );
                    await issueEventsCache.SetAsync(owner, repoName, issueNumber, events);
                    return events;
                }
                catch (ApiException redirectEx)
                {
                    logger.LogWarning(
                        redirectEx,
                        "Failed to get issue events from redirected location {NewOwner}/{NewRepoName}#{NewIssueNumber}: {StatusCode} {Message}",
                        newOwner,
                        newRepoName,
                        newIssueNumber,
                        redirectEx.StatusCode,
                        redirectEx.Content
                    );
                }
            }

            logger.LogWarning(
                ex,
                "Failed to get issue events for {Owner}/{RepoName}#{IssueNumber}: {StatusCode} {Message}",
                owner,
                repoName,
                issueNumber,
                ex.StatusCode,
                ex.Content
            );
            await issueEventsCache.SetAsync(owner, repoName, issueNumber, null);
            return null;
        }
        catch (ApiException ex)
        {
            logger.LogWarning(
                ex,
                "Failed to get issue events for {Owner}/{RepoName}#{IssueNumber}: {StatusCode} {Message}",
                owner,
                repoName,
                issueNumber,
                ex.StatusCode,
                ex.Content
            );
            await issueEventsCache.SetAsync(owner, repoName, issueNumber, null);
            return null;
        }
    }

    public async Task<List<GitHubTimelineEvent>?> GetIssueTimelineAsync(
        string owner,
        string repoName,
        long issueNumber,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await gitHubApi.GetIssueTimelineAsync(
                owner,
                repoName,
                issueNumber,
                cancellationToken: cancellationToken
            );
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogInformation(
                "Timeline not found for {Owner}/{RepoName}#{IssueNumber}: {StatusCode} {Message}",
                owner,
                repoName,
                issueNumber,
                ex.StatusCode,
                ex.Content
            );
            return null;
        }
        catch (ApiException ex)
        {
            logger.LogWarning(
                ex,
                "Failed to get timeline for {Owner}/{RepoName}#{IssueNumber}: {StatusCode} {Message}",
                owner,
                repoName,
                issueNumber,
                ex.StatusCode,
                ex.Content
            );
            return null;
        }
    }

    public async Task<string?> GetRepositoryReadmeAsync(string owner, string repoName)
    {
        var readmeResponse = await gitHubClient.Repos[owner][repoName].Readme.GetAsync();
        return DecodeBase64Content(readmeResponse?.Content);
    }

    public async Task<string?> GetFileContentAsync(string owner, string repoName, string filePath)
    {
        var fileContent = await gitHubApi.GetRepositoryFileContentAsync(owner, repoName, filePath);
        return DecodeBase64Content(fileContent?.Content);
    }

    public async Task<RepositoryTree?> GetRepositoryTreeAsync(
        string owner,
        string repoName,
        bool recursive = true
    )
    {
        try
        {
            var repo = await GetRepositoryInfoAsync(owner, repoName, cancellationToken: default);
            var defaultBranch = repo.DefaultBranch ?? "main";

            var json = await gitHubApi.GetGitTreeAsync(
                owner,
                repoName,
                defaultBranch,
                recursive ? 1 : null
            );
            return json != null
                ? JsonSerializer.Deserialize(json, GitHubAPIJsonContext.Default.RepositoryTree)
                : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not get repository tree for {Owner}/{RepoName}",
                owner,
                repoName
            );
            return null;
        }
    }

    public async Task<GitHubCommit?> GetCommitAsync(
        string owner,
        string repoName,
        string commitSha,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await gitHubApi.GetCommitAsync(owner, repoName, commitSha, cancellationToken);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(
                ex,
                "Failed to fetch commit {CommitSha} for {Owner}/{RepoName}: {StatusCode} {Message}",
                commitSha,
                owner,
                repoName,
                ex.StatusCode,
                ex.Content
            );
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Unexpected error fetching commit {CommitSha} for {Owner}/{RepoName}",
                commitSha,
                owner,
                repoName
            );
            return null;
        }
    }

    public async Task<GitHubPullRequestDetails?> GetPullRequestAsync(
        string owner,
        string repoName,
        int pullNumber,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await gitHubApi.GetPullRequestAsync(
                owner,
                repoName,
                pullNumber,
                cancellationToken
            );
        }
        catch (ApiException ex)
        {
            logger.LogWarning(
                ex,
                "Failed to fetch pull request #{PullNumber} for {Owner}/{RepoName}: {StatusCode} {Message}",
                pullNumber,
                owner,
                repoName,
                ex.StatusCode,
                ex.Content
            );
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Unexpected error fetching pull request #{PullNumber} for {Owner}/{RepoName}",
                pullNumber,
                owner,
                repoName
            );
            return null;
        }
    }

    public async Task<IReadOnlyList<GitHubPullRequestFile>?> GetPullRequestFilesAsync(
        string owner,
        string repoName,
        int pullNumber,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var files = await gitHubApi.GetPullRequestFilesAsync(
                owner,
                repoName,
                pullNumber,
                cancellationToken
            );
            return files?.AsReadOnly();
        }
        catch (ApiException ex)
        {
            logger.LogWarning(
                ex,
                "Failed to fetch files for pull request #{PullNumber} in {Owner}/{RepoName}: {StatusCode} {Message}",
                pullNumber,
                owner,
                repoName,
                ex.StatusCode,
                ex.Content
            );
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Unexpected error fetching files for pull request #{PullNumber} in {Owner}/{RepoName}",
                pullNumber,
                owner,
                repoName
            );
            return null;
        }
    }

    public static string CreateEventDescription(GitHubEvent evt) => evt.Event ?? "Unknown event";

    private async Task<int> GetSearchCountAsync(string query)
    {
        var cachedCount = await searchResultsCache.TryGetAsync(query);
        if (cachedCount.HasValue)
        {
            return cachedCount.Value;
        }

        try
        {
            var json = await gitHubApi.SearchIssuesAsync(query);
            var response = JsonSerializer.Deserialize<GitHubSearchResponse>(
                json,
                GitHubAPIJsonContext.Default.GitHubSearchResponse
            );
            var count = response?.TotalCount ?? 0;
            await searchResultsCache.SetAsync(query, count);
            return count;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            logger.LogWarning(
                "GitHub search validation failed for query '{Query}': {Message}",
                query,
                ex.Content
            );
            await searchResultsCache.SetAsync(query, 0);
            return 0;
        }
    }

    private async Task<(
        string owner,
        string repoName,
        long issueNumber
    )?> GetRedirectedIssueLocationAsync(
        string originalOwner,
        string originalRepoName,
        long originalIssueNumber
    )
    {
        try
        {
            var issueJson = await gitHubApi.GetIssueJsonAsync(
                originalOwner,
                originalRepoName,
                originalIssueNumber
            );
            var issueInfo = JsonSerializer.Deserialize<GitHubIssueRedirect>(
                issueJson,
                GitHubAPIJsonContext.Default.GitHubIssueRedirect
            );

            if (issueInfo?.Url != null)
            {
                var urlParts = issueInfo.Url.Split('/');
                if (urlParts.Length >= 8 && urlParts[^2] == "issues")
                {
                    var newOwner = urlParts[^4];
                    var newRepoName = urlParts[^3];
                    if (long.TryParse(urlParts[^1], out var newIssueNumber))
                    {
                        return (newOwner, newRepoName, newIssueNumber);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(
                ex,
                "Failed to check for redirected issue location for {Owner}/{RepoName}#{IssueNumber}",
                originalOwner,
                originalRepoName,
                originalIssueNumber
            );
        }

        return null;
    }

    private static string? DecodeBase64Content(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return null;

        var bytes = Convert.FromBase64String(content);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}
