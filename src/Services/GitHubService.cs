using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataCollection.Models.IssueTracker;
using Microsoft.Extensions.Logging;
using Octokit;

namespace DataCollection.Services;

public class GitHubService(
    IGitHubClient octokitClient,
    ILogger<GitHubService> logger,
    IsDeveloperCriterion isDeveloperCriterion
)
{
    private readonly Dictionary<long, IReadOnlyList<RepositoryContributor>> repoContributorsCache =
    [];
    private readonly Dictionary<
        (string UserLogin, long RepositoryId),
        UserProfile
    > userProfileCache = [];

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

    private async Task<UserProfile> GetUserProfileAsync(User octokitUser, Repository repository)
    {
        ArgumentNullException.ThrowIfNull(octokitUser);
        ArgumentNullException.ThrowIfNull(repository);

        var cacheKey = (octokitUser.Login, repository.Id);
        if (userProfileCache.TryGetValue(cacheKey, out var cachedProfile))
            return cachedProfile;

        try
        {
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

            return userProfileCache[cacheKey] = basicProfile;
        }
        catch (ApiException ex)
        {
            logger.LogError(
                ex,
                "Failed to get user profile for {Login} in repository {RepoFullName}",
                octokitUser.Login,
                repository.FullName
            );
            throw;
        }
    }

    public async Task<IssueProfile> BuildComprehensiveIssueProfileAsync(
        string owner,
        string repoName,
        long issueNumber
    )
    {
        var repository =
            await octokitClient.Repository.Get(owner, repoName)
            ?? throw new ArgumentException($"Repository {owner}/{repoName} not found");

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

        var authorProfile = await GetUserProfileAsync(issue.User, repository);
        var labelEvents = new List<LabelEventProfile>();

        foreach (var evt in issueEvents.Where(e => e.Label != null && e.Actor != null))
            labelEvents.Add(
                new LabelEventProfile
                {
                    Label = evt.Label,
                    By = await GetUserProfileAsync(evt.Actor, repository),
                    OccuredAt = evt.CreatedAt,
                    Event = evt.Event.StringValue switch
                    {
                        "labeled" => LabelEvent.Added,
                        "unlabeled" => LabelEvent.Removed,
                        _ => throw new InvalidOperationException(
                            $"Unknown label event: {evt.Event.StringValue}"
                        ),
                    },
                }
            );

        var commentEvents = new List<CommentEventProfile>();
        foreach (var comment in comments.Where(c => c.User != null))
            commentEvents.Add(
                new CommentEventProfile
                {
                    Comment = comment,
                    By = await GetUserProfileAsync(comment.User, repository),
                }
            );

        return new IssueProfile
        {
            OctokitIssue = issue,
            AuthorProfile = authorProfile,
            RepositoryFullName = repository.FullName,
            IsClosed = issue.State.Value == ItemState.Closed,
            HasAssociatedPullRequest = issue.PullRequest != null,
            LabelEvents = [.. labelEvents],
            CommentEvents = [.. commentEvents],
        };
    }
}
