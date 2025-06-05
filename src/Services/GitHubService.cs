using System.Collections.Concurrent;
using ConsoleAppFramework;
using DataCollection.Models.GitHub;
using DataCollection.Models.IssueTracker.Criteria;
using DataCollection.Models.IssueTracker.Profiles;
using DataCollection.Options;
using DataCollection.Serialization;
using GitHub;
using GitHub.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace DataCollection.Services;

[ConsoleAppFilter<PathsOptions.Filter>]
public class GitHubService(
    GitHubClient gitHubClient,
    IGitHubApi gitHubApi,
    ILogger<GitHubService> logger,
    IsDeveloperCriterion isDeveloperCriterion,
    IOptions<PathsOptions> pathsOptions
)
{
    private readonly ConcurrentDictionary<long, IReadOnlyList<Contributor>> repoContributorsCache =
    [];
    private readonly ConcurrentDictionary<
        (string UserLogin, long RepositoryId),
        UserProfile
    > userProfileCache = [];
    private readonly ConcurrentDictionary<string, FullRepository> repositoryCache = [];

    /// <summary>
    /// Gets information about a repository and caches it
    /// </summary>
    /// <param name="owner">Repository owner</param>
    /// <param name="repoName">Repository name</param>
    /// <returns>Repository information</returns>
    public async Task<FullRepository> GetRepositoryInfoAsync(string owner, string repoName)
    {
        var cacheKey = $"{owner}/{repoName}";
        if (repositoryCache.TryGetValue(cacheKey, out var cachedRepo))
            return cachedRepo;

        // Check disk cache
        var repoDir = Path.Combine(pathsOptions.Value.IssueRepoDir, owner);
        var repoFile = Path.Combine(repoDir, $"{repoName}.json");

        if (File.Exists(repoFile))
        {
            try
            {
                var json = await File.ReadAllTextAsync(repoFile);
                // System.Text.Json doesn't support deserializing into GitHub.Models types... have to use Newtonsoft.Json
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

            // Cache to disk
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

    /// <summary>
    /// Gets a user's profile in the context of a specific repository
    /// </summary>
    /// <param name="login">GitHub username</param>
    /// <param name="repository">Repository context</param>
    /// <returns>User profile</returns>
    public async Task<UserProfile> GetUserProfileAsync(string login, FullRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        // Check memory cache first
        var cacheKey = (login, repository.Id.GetValueOrDefault());
        if (userProfileCache.TryGetValue(cacheKey, out var cachedProfile))
            return cachedProfile;

        // Check disk cache
        var usersDir = Path.Combine(pathsOptions.Value.IssueProfileDir, "users");
        if (!Directory.Exists(usersDir))
            Directory.CreateDirectory(usersDir);

        // Include repo ID in the file path to avoid overwriting profiles from different repos
        var repoUserDir = Path.Combine(usersDir, repository.Id.GetValueOrDefault().ToString());
        if (!Directory.Exists(repoUserDir))
            Directory.CreateDirectory(repoUserDir);

        var userFile = Path.Combine(repoUserDir, $"{login}.json");

        if (File.Exists(userFile))
        {
            try
            {
                var json = await File.ReadAllTextAsync(userFile);
                var profile = JsonSerializer.Deserialize<UserProfile>(
                    json,
                    GitHubAPIJsonContext.Default.UserProfile
                );
                if (profile != null)
                {
                    userProfileCache[cacheKey] = profile;
                    return profile;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to read cached user profile for {Login} in repo {RepoId}, deleting",
                    login,
                    repository.Id
                );
                File.Delete(userFile);
            }
        }

        var user = await gitHubClient.Users[login].GetAsync();
        if (user == null)
            throw new InvalidOperationException($"User {login} not found");

        var userProfile = await GetUserProfileAsync(login, user, repository);

        // Save to disk cache
        try
        {
            var json = JsonSerializer.Serialize(
                userProfile,
                GitHubAPIJsonContext.Default.WithUsernameGetResponse
            );
            await File.WriteAllTextAsync(userFile, json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to write user profile to disk for {Login} in repo {RepoId}",
                login,
                repository.Id
            );
        }

        return userProfile;
    }

    private async Task<bool> IsUserContributorAsync(string userLogin, FullRepository repository)
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

    /// <summary>
    /// Gets a user's profile in the context of a specific repository
    /// </summary>
    /// <param name="userLogin">User login</param>
    /// <param name="sdkUser">User information from SDK</param>
    /// <param name="repository">Repository context</param>
    /// <returns>User profile with repository-specific metrics</returns>
    public async Task<UserProfile> GetUserProfileAsync(
        string userLogin,
        object sdkUser, // Using object since the actual return type from Users[].GetAsync() might vary
        FullRepository repository
    )
    {
        ArgumentNullException.ThrowIfNull(userLogin);
        ArgumentNullException.ThrowIfNull(repository);

        var cacheKey = (userLogin, repository.Id.GetValueOrDefault());
        if (userProfileCache.TryGetValue(cacheKey, out var cachedProfile))
            return cachedProfile;

        var isContributorTask = IsUserContributorAsync(userLogin, repository);

        // Note: The new SDK might have different search endpoints. This is a simplified approach
        // and may need adjustment based on actual SDK API surface
        var issuesTask = GetUserIssuesCountAsync(userLogin, repository);
        var prsTask = GetUserPullRequestsAsync(userLogin, repository);

        await Task.WhenAll(isContributorTask, issuesTask, prsTask);

        var issuesCount = await issuesTask;
        var (prsCount, mergedPrsCount) = await prsTask;

        // TODO: fetch collaborator/member status. Currently they're only from issue comments.
        var basicProfile = new UserProfile
        {
            Login = userLogin,
            IsContributor = await isContributorTask,
            TotalIssues = issuesCount,
            TotalPullRequests = prsCount,
            TotalMergedPullRequests = mergedPrsCount,
        };
        var isDeveloper = await isDeveloperCriterion.EvaluateAsync(basicProfile);
        basicProfile.IsDeveloper = isDeveloper;
        logger.LogDebug("User {Profile}", basicProfile);

        userProfileCache[cacheKey] = basicProfile;
        return basicProfile;
    }

    // Helper method to get user issues count - using GitHub Search API for efficiency
    private async Task<int> GetUserIssuesCountAsync(string userLogin, FullRepository repository)
    {
        try
        {
            // Use GitHub's search API to efficiently count issues by user in the repository
            // Search for: "type:issue author:username repo:owner/name"
            var searchQuery =
                $"type:issue author:{userLogin} repo:{repository.Owner?.Login}/{repository.Name}";

            var searchResult = await gitHubClient.Search.Issues.GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Q = searchQuery;
            });

            return searchResult?.TotalCount ?? 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to get issues count for user {User} in repo {Repo} using search API",
                userLogin,
                repository.FullName
            );
            return 0;
        }
    }

    // Helper method to get user pull requests - using GitHub Search API for efficiency
    private async Task<(int total, int merged)> GetUserPullRequestsAsync(
        string userLogin,
        FullRepository repository
    )
    {
        try
        {
            // Use GitHub's search API to efficiently count PRs by user in the repository
            // Search for: "type:pr author:username repo:owner/name"
            var totalSearchQuery =
                $"type:pr author:{userLogin} repo:{repository.Owner?.Login}/{repository.Name}";

            var totalSearchResult = await gitHubClient.Search.Issues.GetAsync(
                requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Q = totalSearchQuery;
                }
            );

            var totalCount = totalSearchResult?.TotalCount ?? 0;

            // Search for merged PRs: "type:pr author:username repo:owner/name is:merged"
            var mergedSearchQuery =
                $"type:pr author:{userLogin} repo:{repository.Owner?.Login}/{repository.Name} is:merged";

            var mergedSearchResult = await gitHubClient.Search.Issues.GetAsync(
                requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Q = mergedSearchQuery;
                }
            );

            var mergedCount = mergedSearchResult?.TotalCount ?? 0;

            return (totalCount, mergedCount);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to get pull requests for user {User} in repo {Repo} using search API",
                userLogin,
                repository.FullName
            );
            return (0, 0);
        }
    }

    public async Task<IssueProfile> BuildComprehensiveIssueProfileAsync(
        string owner,
        string repoName,
        long issueNumber
    )
    {
        var repository = await GetRepositoryInfoAsync(owner, repoName);

        var issue = await gitHubClient.Repos[owner][repoName].Issues[(int)issueNumber].GetAsync();
        if (issue == null)
            throw new ArgumentException(
                $"Issue {issueNumber} not found in repository {owner}/{repoName}"
            );

        // Use Refit API client to avoid SDK integer overflow bug
        var issueEvents = await gitHubApi.GetIssueEventsAsync(owner, repoName, issueNumber) ?? [];
        var comments = await gitHubApi.GetIssueCommentsAsync(owner, repoName, issueNumber) ?? [];

        // Try to use cached user profile for author first
        var authorProfile = await GetUserProfileAsync(
            issue.User?.Login ?? throw new InvalidOperationException("Issue user is null"),
            issue.User,
            repository
        );

        var labelEvents = new List<LabelEventProfile>();
        var otherEvents = new List<OtherEventProfile>();

        // Process events with proper event type handling
        foreach (var evt in issueEvents)
        {
            if (evt == null)
                continue;

            if (evt.Event?.ToLowerInvariant() is "labeled" or "unlabeled" && evt.Label != null)
            {
                labelEvents.Add(
                    new LabelEventProfile
                    {
                        SdkLabel = new Label { Name = evt.Label.Name, Color = evt.Label.Color },
                        Event =
                            evt.Event.ToLowerInvariant() == "labeled"
                                ? LabelEventType.Added
                                : LabelEventType.Removed,
                        OccuredAt = evt.CreatedAt,
                        By =
                            evt.Actor?.Login != null
                                ? await GetUserProfileAsync(evt.Actor.Login, evt.Actor, repository)
                                : authorProfile,
                    }
                );
            }
            else
            {
                otherEvents.Add(
                    new OtherEventProfile
                    {
                        EventType = evt.Event ?? "unknown",
                        By =
                            evt.Actor?.Login != null
                                ? await GetUserProfileAsync(evt.Actor.Login, evt.Actor, repository)
                                : authorProfile,
                        OccurredAt = evt.CreatedAt,
                        EventDescription = JsonSerializer.Serialize(
                            evt.ExtractDetails(),
                            GitHubAPIJsonContext.Default.GitHubEventDetails
                        ),
                    }
                );
            }
        }

        var commentEvents = new List<CommentEventProfile>();
        foreach (var comment in comments)
        {
            if (comment?.User?.Login == null)
                continue;

            var profile = await GetUserProfileAsync(comment.User.Login, comment.User, repository);

            commentEvents.Add(new CommentEventProfile { SdkComment = comment, By = profile });
        }

        return new IssueProfile
        {
            SdkIssue = issue,
            SdkRepository = repository,
            AuthorProfile = authorProfile,
            RepositoryFullName = repository.FullName ?? $"{owner}/{repoName}",
            IsClosed = issue.State?.ToString() == "closed",
            HasAssociatedPullRequest = issue.PullRequest != null,
            LabelEvents = [.. labelEvents],
            CommentEvents = [.. commentEvents],
            OtherEvents = [.. otherEvents],
        };
    }

    /// <summary>
    /// Gets the README content of a repository
    /// </summary>
    /// <param name="owner">Repository owner</param>
    /// <param name="repoName">Repository name</param>
    /// <returns>README content or null if not found</returns>
    public async Task<string?> GetRepositoryReadmeAsync(string owner, string repoName)
    {
        try
        {
            // Try common README file names
            var readmeNames = new[] { "README.md", "readme.md", "README.MD", "README", "readme" };

            foreach (var readmeName in readmeNames)
            {
                var readmeContent = await GetFileContentAsync(owner, repoName, readmeName);
                if (!string.IsNullOrEmpty(readmeContent))
                {
                    logger.LogDebug(
                        "Found README for {Owner}/{RepoName}: {ReadmeName}",
                        owner,
                        repoName,
                        readmeName
                    );
                    return readmeContent;
                }
            }

            logger.LogDebug("No README found for {Owner}/{RepoName}", owner, repoName);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get README for {Owner}/{RepoName}", owner, repoName);
            return null;
        }
    }

    /// <summary>
    /// Gets the repository directory tree
    /// </summary>
    /// <param name="owner">Repository owner</param>
    /// <param name="repoName">Repository name</param>
    /// <param name="recursive">Whether to get tree recursively</param>
    /// <returns>Repository tree</returns>
    public async Task<RepositoryTree?> GetRepositoryTreeAsync(
        string owner,
        string repoName,
        bool recursive = true
    )
    {
        try
        {
            var repo = await GetRepositoryInfoAsync(owner, repoName);
            var defaultBranch = repo.DefaultBranch ?? "main";

            var url = $"repos/{owner}/{repoName}/git/trees/{defaultBranch}";
            if (recursive)
                url += "?recursive=1";

            var json = await gitHubApi.GetJsonAsync(url);
            return json != null
                ? JsonSerializer.Deserialize<RepositoryTree>(
                    json,
                    GitHubAPIJsonContext.Default.RepositoryTree
                )
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

    /// <summary>
    /// Gets the content of a specific file in the repository
    /// </summary>
    /// <param name="owner">Repository owner</param>
    /// <param name="repoName">Repository name</param>
    /// <param name="filePath">Path to the file</param>
    /// <returns>File content as string</returns>
    public async Task<string?> GetFileContentAsync(string owner, string repoName, string filePath)
    {
        try
        {
            var fileContent = await gitHubApi.GetRepositoryFileContentAsync(
                owner,
                repoName,
                filePath
            );

            if (fileContent?.Content != null && fileContent.Encoding == "base64")
            {
                var bytes = Convert.FromBase64String(fileContent.Content.Replace("\n", ""));
                return System.Text.Encoding.UTF8.GetString(bytes);
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(
                ex,
                "Failed to get file content for {Owner}/{RepoName}/{FilePath}",
                owner,
                repoName,
                filePath
            );
            return null;
        }
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
}
