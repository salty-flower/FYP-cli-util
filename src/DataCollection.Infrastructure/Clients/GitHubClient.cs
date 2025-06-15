using System.Collections.Concurrent;
using DataCollection.Infrastructure.Models.GitHub;
using DataCollection.Infrastructure.Options;
using GitHub;
using GitHub;
using GitHub.Models;
using GitHub.Octokit.Client;
using GitHub.Octokit.Client.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace DataCollection.Infrastructure.Clients;

public class GitHubSearchResponse
{
    public int TotalCount { get; set; }
    public bool IncompleteResults { get; set; }
}

public class GitHubClient(
    GitHub.GitHubClient gitHubClient,
    IGitHubApi gitHubApi,
    ILogger<GitHubClient> logger,
    IOptions<PathsOptions> pathsOptions
)
{
    private readonly ConcurrentDictionary<long, IReadOnlyList<Contributor>> repoContributorsCache =
    [];
    private readonly ConcurrentDictionary<string, FullRepository> repositoryCache = [];
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

    public async Task<FullRepository> GetRepositoryInfoAsync(string owner, string repoName)
    {
        var cacheKey = $"{owner}/{repoName}";
        if (repositoryCache.TryGetValue(cacheKey, out var cachedRepo))
            return cachedRepo;

        var repoDir = Path.Combine(pathsOptions.Value.IssueRepoDir, owner);
        var repoFile = Path.Combine(repoDir, $"{repoName}.json");

        if (File.Exists(repoFile))
        {
            try
            {
                var json = await File.ReadAllTextAsync(repoFile);
                var repo = JsonConvert.DeserializeObject<FullRepository>(json);
                if (repo != null && repo.Id != 0)
                {
                    repositoryCache[cacheKey] = repo;
                    return repo;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to read cached repository info for {Owner}/{RepoName}",
                    owner,
                    repoName
                );
            }
        }

        try
        {
            var repository = await gitHubClient.Repos[owner][repoName].GetAsync();
            if (repository == null)
                throw new InvalidOperationException($"Repository {owner}/{repoName} not found");

            repositoryCache[cacheKey] = repository;

            if (!Directory.Exists(repoDir))
                Directory.CreateDirectory(repoDir);

            var json = JsonSerializer.Serialize(repository);
            await File.WriteAllTextAsync(repoFile, json);

            return repository;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to get repository information for {Owner}/{RepoName}",
                owner,
                repoName
            );
            throw;
        }
    }

    public async Task<object> GetUserAsync(string userLogin)
    {
        var cacheKey = userLogin;
        if (userCache.TryGetValue(cacheKey, out var cachedUser))
            return cachedUser;

        try
        {
            var user = await gitHubClient.Users[userLogin].GetAsync();
            if (user == null)
                throw new InvalidOperationException($"User {userLogin} not found");

            userCache[cacheKey] = user;
            return user;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get user information for {UserLogin}", userLogin);
            throw;
        }
    }

    public async Task<bool> IsUserContributorAsync(string userLogin, FullRepository repository)
    {
        var repoId = repository.Id.GetValueOrDefault();
        if (!repoContributorsCache.TryGetValue(repoId, out var contributors))
        {
            var contributorsResponse = await gitHubClient
                .Repos[
                    repository.Owner?.Login
                        ?? throw new InvalidOperationException("Repository owner is null")
                ][repository.Name ?? throw new InvalidOperationException("Repository name is null")]
                .Contributors.GetAsync();
            contributors = contributorsResponse ?? new List<Contributor>();

            repoContributorsCache[repoId] = contributors.ToList().AsReadOnly();
        }

        return contributors.Any(c => c.Login == userLogin);
    }

    public async Task<int> GetUserIssuesCountAsync(string userLogin, FullRepository repository)
    {
        try
        {
            var searchQuery =
                $"type:issue author:{userLogin} repo:{repository.Owner?.Login}/{repository.Name}";
            var searchJson = await gitHubApi.GetJsonAsync(
                $"search/issues?q={Uri.EscapeDataString(searchQuery)}"
            );

            var searchResponse = JsonSerializer.Deserialize<GitHubSearchResponse>(searchJson);
            return searchResponse?.TotalCount ?? 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to get issues count for user {UserLogin} in repository {Repository}",
                userLogin,
                $"{repository.Owner?.Login}/{repository.Name}"
            );
            return 0;
        }
    }

    public async Task<(int total, int merged)> GetUserPullRequestsAsync(
        string userLogin,
        FullRepository repository
    )
    {
        try
        {
            var totalSearchQuery =
                $"type:pr author:{userLogin} repo:{repository.Owner?.Login}/{repository.Name}";
            var totalJson = await gitHubApi.GetJsonAsync(
                $"search/issues?q={Uri.EscapeDataString(totalSearchQuery)}"
            );
            var totalResponse = JsonSerializer.Deserialize<GitHubSearchResponse>(totalJson);
            var totalCount = totalResponse?.TotalCount ?? 0;

            var mergedSearchQuery =
                $"type:pr author:{userLogin} repo:{repository.Owner?.Login}/{repository.Name} is:merged";
            var mergedJson = await gitHubApi.GetJsonAsync(
                $"search/issues?q={Uri.EscapeDataString(mergedSearchQuery)}"
            );
            var mergedResponse = JsonSerializer.Deserialize<GitHubSearchResponse>(mergedJson);
            var mergedCount = mergedResponse?.TotalCount ?? 0;

            return (totalCount, mergedCount);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to get PR counts for user {UserLogin} in repository {Repository}",
                userLogin,
                $"{repository.Owner?.Login}/{repository.Name}"
            );
            return (0, 0);
        }
    }

    public async Task<Issue?> GetIssueAsync(string owner, string repoName, long issueNumber)
    {
        try
        {
            return await gitHubClient.Repos[owner][repoName].Issues[(int)issueNumber].GetAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to get issue {Owner}/{RepoName}#{IssueNumber}",
                owner,
                repoName,
                issueNumber
            );
            throw;
        }
    }

    public async Task<List<IssueComment>?> GetIssueCommentsAsync(
        string owner,
        string repoName,
        long issueNumber
    )
    {
        try
        {
            return await gitHubApi.GetIssueCommentsAsync(owner, repoName, issueNumber);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to get comments for issue {Owner}/{RepoName}#{IssueNumber}",
                owner,
                repoName,
                issueNumber
            );
            return null;
        }
    }

    public async Task<List<GitHubEvent>?> GetIssueEventsAsync(
        string owner,
        string repoName,
        long issueNumber
    )
    {
        try
        {
            return await gitHubApi.GetIssueEventsAsync(owner, repoName, issueNumber);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to get events for issue {Owner}/{RepoName}#{IssueNumber}",
                owner,
                repoName,
                issueNumber
            );
            return null;
        }
    }

    public async Task<string?> GetRepositoryReadmeAsync(string owner, string repoName)
    {
        try
        {
            var readmeResponse = await gitHubClient.Repos[owner][repoName].Readme.GetAsync();
            if (readmeResponse?.Content == null)
                return null;

            var bytes = Convert.FromBase64String(readmeResponse.Content);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to get README for repository {Owner}/{RepoName}",
                owner,
                repoName
            );
            return null;
        }
    }

    public async Task<string?> GetFileContentAsync(string owner, string repoName, string filePath)
    {
        try
        {
            var fileContent = await gitHubApi.GetRepositoryFileContentAsync(
                owner,
                repoName,
                filePath
            );
            if (fileContent?.Content == null)
                return null;

            var bytes = Convert.FromBase64String(fileContent.Content);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to get file content for {Owner}/{RepoName}/{FilePath}",
                owner,
                repoName,
                filePath
            );
            return null;
        }
    }

    public static string CreateEventDescription(GitHubEvent evt)
    {
        return evt.Event?.ToString() ?? "Unknown event";
    }
}
