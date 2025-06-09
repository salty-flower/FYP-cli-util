using System.Collections.Concurrent;
using DataCollection.Application.Features.IssueAnalysis.Rules;
using DataCollection.Application.Models.IssueTracker.Profiles;
using DataCollection.Infrastructure.Clients;
using DataCollection.Infrastructure.Models.GitHub;
using DataCollection.Infrastructure.Options;
using DataCollection.Infrastructure.Serialization;
using GitHub.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace DataCollection.Application.Features.BugDiscovery;

public class GitHubProfileService(
    GitHubClient gitHubClient,
    ILogger<GitHubProfileService> logger,
    IsDeveloperCriterion isDeveloperCriterion,
    IOptions<PathsOptions> pathsOptions
)
{
    private readonly ConcurrentDictionary<
        (string UserLogin, long RepositoryId),
        UserProfile
    > userProfileCache = [];

    public async Task<UserProfile> GetUserProfileAsync(string login, FullRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var cacheKey = (login, repository.Id.GetValueOrDefault());
        if (userProfileCache.TryGetValue(cacheKey, out var cachedProfile))
            return cachedProfile;

        var usersDir = Path.Combine(pathsOptions.Value.IssueProfileDir, "users");
        if (!Directory.Exists(usersDir))
            Directory.CreateDirectory(usersDir);

        var repoUserDir = Path.Combine(usersDir, repository.Id.GetValueOrDefault().ToString());
        if (!Directory.Exists(repoUserDir))
            Directory.CreateDirectory(repoUserDir);

        var userFile = Path.Combine(repoUserDir, $"{login}.json");

        if (File.Exists(userFile))
        {
            try
            {
                var json = await File.ReadAllTextAsync(userFile);
                var profile = JsonSerializer.Deserialize<UserProfile>(json);
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

        var user = await gitHubClient.GetUserAsync(login);
        if (user == null)
            throw new InvalidOperationException($"User {login} not found");

        var userProfile = await BuildUserProfileAsync(login, user, repository);

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

    public async Task<UserProfile> BuildUserProfileAsync(
        string userLogin,
        object sdkUser,
        FullRepository repository
    )
    {
        ArgumentNullException.ThrowIfNull(userLogin);
        ArgumentNullException.ThrowIfNull(repository);

        var cacheKey = (userLogin, repository.Id.GetValueOrDefault());
        if (userProfileCache.TryGetValue(cacheKey, out var cachedProfile))
            return cachedProfile;

        var isContributorTask = gitHubClient.IsUserContributorAsync(userLogin, repository);
        var issuesTask = gitHubClient.GetUserIssuesCountAsync(userLogin, repository);
        var prsTask = gitHubClient.GetUserPullRequestsAsync(userLogin, repository);

        await Task.WhenAll(isContributorTask, issuesTask, prsTask);

        var issuesCount = await issuesTask;
        var (prsCount, mergedPrsCount) = await prsTask;

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

    public async Task<IssueProfile> BuildComprehensiveIssueProfileAsync(
        string owner,
        string repoName,
        long issueNumber
    )
    {
        try
        {
            var repository = await gitHubClient.GetRepositoryInfoAsync(owner, repoName);
            var issue = await gitHubClient.GetIssueAsync(owner, repoName, issueNumber);

            if (issue == null)
                throw new InvalidOperationException(
                    $"Issue {owner}/{repoName}#{issueNumber} not found"
                );

            var authorProfile =
                issue.User?.Login != null
                    ? await GetUserProfileAsync(issue.User.Login, repository)
                    : new UserProfile
                    {
                        Login = "unknown",
                        IsContributor = false,
                        TotalIssues = 0,
                        TotalPullRequests = 0,
                        TotalMergedPullRequests = 0,
                    };

            var comments = await gitHubClient.GetIssueCommentsAsync(owner, repoName, issueNumber);
            var events = await gitHubClient.GetIssueEventsAsync(owner, repoName, issueNumber);

            var commentEventProfiles = new List<CommentEventProfile>();
            if (comments != null)
            {
                foreach (var comment in comments)
                {
                    if (comment.User?.Login != null)
                    {
                        var profile = await GetUserProfileAsync(comment.User.Login, repository);
                        commentEventProfiles.Add(
                            new CommentEventProfile { SdkComment = comment, By = profile }
                        );
                    }
                }
            }

            var labelEventProfiles = new List<LabelEventProfile>();
            var otherEventProfiles = new List<OtherEventProfile>();

            if (events != null)
            {
                foreach (var evt in events)
                {
                    if (evt.Actor?.Login != null)
                    {
                        var actorProfile = await GetUserProfileAsync(evt.Actor.Login, repository);

                        // Handle label events specifically
                        if (
                            evt.Event?.ToString() == "labeled"
                            || evt.Event?.ToString() == "unlabeled"
                        )
                        {
                            if (evt.Label != null)
                            {
                                // Convert GitHubLabel to GitHub SDK Label
                                var sdkLabel = new GitHub.Models.Label
                                {
                                    Name = evt.Label.Name,
                                    Color = evt.Label.Color,
                                };

                                labelEventProfiles.Add(
                                    new LabelEventProfile
                                    {
                                        OccuredAt = evt.CreatedAt,
                                        SdkLabel = sdkLabel,
                                        By = actorProfile,
                                        Event =
                                            evt.Event?.ToString() == "labeled"
                                                ? LabelEventType.Added
                                                : LabelEventType.Removed,
                                    }
                                );
                            }
                        }
                        else
                        {
                            // Handle other events
                            var description = GitHubClient.CreateEventDescription(evt);
                            otherEventProfiles.Add(
                                new OtherEventProfile
                                {
                                    EventType = evt.Event?.ToString() ?? "Unknown",
                                    EventDescription = description,
                                    OccurredAt = evt.CreatedAt,
                                    By = actorProfile,
                                }
                            );
                        }
                    }
                }
            }

            return new IssueProfile
            {
                SdkIssue = issue,
                SdkRepository = repository,
                AuthorProfile = authorProfile,
                RepositoryFullName = repository.FullName ?? $"{owner}/{repoName}",
                IsClosed = issue.State?.ToString()?.ToLower() == "closed",
                HasAssociatedPullRequest = false, // Would need additional logic to determine
                LabelEvents = labelEventProfiles.ToArray(),
                CommentEvents = commentEventProfiles.ToArray(),
                OtherEvents = otherEventProfiles.ToArray(),
            };
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to build comprehensive issue profile for {Owner}/{RepoName}#{IssueNumber}",
                owner,
                repoName,
                issueNumber
            );
            throw;
        }
    }
}
