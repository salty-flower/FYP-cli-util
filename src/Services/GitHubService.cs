using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ConsoleAppFramework;
using DataCollection.Models.IssueTracker.Criteria;
using DataCollection.Models.IssueTracker.Profiles;
using DataCollection.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Octokit;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace DataCollection.Services;

[ConsoleAppFilter<PathsOptions.Filter>]
public class GitHubService(
    IGitHubClient octokitClient,
    ILogger<GitHubService> logger,
    IsDeveloperCriterion isDeveloperCriterion,
    IOptions<PathsOptions> pathsOptions
)
{
    private readonly ConcurrentDictionary<
        long,
        IReadOnlyList<RepositoryContributor>
    > repoContributorsCache = [];
    private readonly ConcurrentDictionary<
        (string UserLogin, long RepositoryId),
        UserProfile
    > userProfileCache = [];
    private readonly ConcurrentDictionary<string, Repository> repositoryCache = [];
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Gets information about a repository and caches it
    /// </summary>
    /// <param name="owner">Repository owner</param>
    /// <param name="repoName">Repository name</param>
    /// <returns>Repository information</returns>
    public async Task<Repository> GetRepositoryInfoAsync(string owner, string repoName)
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
                // System.Text.Json doesn't support deserializing into Octokit types... have to use Newtonsoft.Json
                var repo = JsonConvert.DeserializeObject<Repository>(json);
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
            var repository = await octokitClient.Repository.Get(owner, repoName);
            repositoryCache[cacheKey] = repository;

            // Cache to disk
            if (!Directory.Exists(repoDir))
                Directory.CreateDirectory(repoDir);

            var json = JsonSerializer.Serialize(repository, jsonOptions);
            await File.WriteAllTextAsync(repoFile, json);

            return repository;
        }
        catch (ApiException ex)
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
    public async Task<UserProfile> GetUserProfileAsync(string login, Repository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        // Check memory cache first
        var cacheKey = (login, repository.Id);
        if (userProfileCache.TryGetValue(cacheKey, out var cachedProfile))
            return cachedProfile;

        // Check disk cache
        var usersDir = Path.Combine(pathsOptions.Value.IssueProfileDir, "users");
        if (!Directory.Exists(usersDir))
            Directory.CreateDirectory(usersDir);

        // Include repo ID in the file path to avoid overwriting profiles from different repos
        var repoUserDir = Path.Combine(usersDir, repository.Id.ToString());
        if (!Directory.Exists(repoUserDir))
            Directory.CreateDirectory(repoUserDir);

        var userFile = Path.Combine(repoUserDir, $"{login}.json");

        if (File.Exists(userFile))
        {
            try
            {
                var json = await File.ReadAllTextAsync(userFile);
                var profile = JsonSerializer.Deserialize<UserProfile>(json, jsonOptions);
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

        var user = await octokitClient.User.Get(login);

        var userProfile = await GetUserProfileAsync(user, repository);

        // Save to disk cache
        try
        {
            var json = JsonSerializer.Serialize(userProfile, jsonOptions);
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

    private async Task<bool> IsUserContributorAsync(User user, Repository repository)
    {
        if (!repoContributorsCache.TryGetValue(repository.Id, out var contributors))
        {
            contributors = await octokitClient.Repository.GetAllContributors(repository.Id);
            repoContributorsCache[repository.Id] = contributors
                .Where(c => c.Login != null)
                .ToList()
                .AsReadOnly();
        }

        return contributors.Any(c => c.Login == user.Login);
    }

    /// <summary>
    /// Gets a user's profile in the context of a specific repository
    /// </summary>
    /// <param name="octokitUser">User information</param>
    /// <param name="repository">Repository context</param>
    /// <returns>User profile with repository-specific metrics</returns>
    public async Task<UserProfile> GetUserProfileAsync(User octokitUser, Repository repository)
    {
        ArgumentNullException.ThrowIfNull(octokitUser);
        ArgumentNullException.ThrowIfNull(repository);

        var cacheKey = (octokitUser.Login, repository.Id);
        if (userProfileCache.TryGetValue(cacheKey, out var cachedProfile))
            return cachedProfile;

        var isContributorTask = IsUserContributorAsync(octokitUser, repository);

        var issuesTask = octokitClient.Search.SearchIssues(
            new SearchIssuesRequest
            {
                Author = octokitUser.Login,
                Type = IssueTypeQualifier.Issue,
                Repos = [$"{repository.Owner.Login}/{repository.Name}"],
            }
        );
        var prsTask = octokitClient.Search.SearchIssues(
            new SearchIssuesRequest
            {
                Author = octokitUser.Login,
                Type = IssueTypeQualifier.PullRequest,
                Repos = [$"{repository.Owner.Login}/{repository.Name}"],
            }
        );

        await Task.WhenAll(isContributorTask, issuesTask, prsTask);
        var prs = await prsTask;

        // TODO: fetch collaborator/member status. Currently they're only from issue comments.
        var basicProfile = new UserProfile
        {
            Login = octokitUser.Login,
            IsContributor = await isContributorTask,
            TotalIssues = (await issuesTask).TotalCount,
            TotalPullRequests = prs.TotalCount,
            TotalMergedPullRequests = prs.Items.Count(i => i.PullRequest.Merged),
        };
        var isDeveloper = await isDeveloperCriterion.EvaluateAsync(basicProfile);
        basicProfile.IsDeveloper = isDeveloper;
        logger.LogDebug("User {Profile}", basicProfile);

        userProfileCache[cacheKey] = basicProfile;
        return basicProfile;
    }

    public async Task<IssueProfile> BuildComprehensiveIssueProfileAsync(
        string owner,
        string repoName,
        long issueNumber
    )
    {
        var repository = await GetRepositoryInfoAsync(owner, repoName);

        var issue =
            await octokitClient.Issue.Get(repository.Id, issueNumber)
            ?? throw new ArgumentException(
                $"Issue {issueNumber} not found in repository {owner}/{repoName}"
            );

        var issueEvents = await octokitClient.Issue.Events.GetAllForIssue(
            repository.Id,
            issue.Number
        );
        var comments = await octokitClient.Issue.Comment.GetAllForIssue(
            repository.Id,
            issue.Number
        );

        // Try to use cached user profile for author first
        var authorProfile = await GetUserProfileAsync(issue.User.Login, repository);

        var labelEvents = new List<LabelEventProfile>();
        foreach (var evt in issueEvents.Where(e => e.Label != null && e.Actor != null))
            labelEvents.Add(
                new LabelEventProfile
                {
                    Label = evt.Label,
                    By = await GetUserProfileAsync(evt.Actor.Login, repository),
                    OccuredAt = evt.CreatedAt,
                    Event = evt.Event.StringValue switch
                    {
                        "labeled" => LabelEventType.Added,
                        "unlabeled" => LabelEventType.Removed,
                        _ => throw new InvalidOperationException(
                            $"Unknown label event: {evt.Event.StringValue}"
                        ),
                    },
                }
            );

        var commentEvents = new List<CommentEventProfile>();
        foreach (var comment in comments.Where(c => c.User != null))
        {
            var profile = await GetUserProfileAsync(comment.User.Login, repository);
            profile.IsContributor = comment.AuthorAssociation == AuthorAssociation.Contributor;
            profile.IsCollaboratorOrMember =
                comment.AuthorAssociation == AuthorAssociation.Collaborator
                || comment.AuthorAssociation == AuthorAssociation.Member;
            commentEvents.Add(new CommentEventProfile { Comment = comment, By = profile });
        }

        var otherEvents = new List<OtherEventProfile>();
        foreach (var evt in issueEvents.Where(e => e.Label == null && e.Actor != null))
            otherEvents.Add(
                new OtherEventProfile
                {
                    EventType = evt.Event.StringValue,
                    By = await GetUserProfileAsync(evt.Actor.Login, repository),
                    OccurredAt = evt.CreatedAt,
                    EventDescription = JsonConvert.SerializeObject(
                        // only include interested properties
                        new
                        {
                            evt.Rename,
                            evt.ProjectCard,
                            evt.RequestedReviewer,
                            evt.ReviewRequester,
                            evt.Assigner,
                            evt.Assignee,
                            LockReason = string.IsNullOrWhiteSpace(evt.LockReason.StringValue)
                                ? null
                                : evt.LockReason.StringValue,
                            evt.DismissedReview,
                            evt.Milestone,
                        },
                        new JsonSerializerSettings
                        {
                            NullValueHandling = NullValueHandling.Ignore,
                            Formatting = Formatting.Indented,
                        }
                    ),
                }
            );

        return new IssueProfile
        {
            OctokitIssue = issue,
            OctokitRepository = repository,
            AuthorProfile = authorProfile,
            RepositoryFullName = repository.FullName,
            IsClosed = issue.State.Value == ItemState.Closed,
            HasAssociatedPullRequest = issue.PullRequest != null,
            LabelEvents = [.. labelEvents],
            CommentEvents = [.. commentEvents],
            OtherEvents = [.. otherEvents],
        };
    }
}
