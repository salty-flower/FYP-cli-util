using DataCollection.Application.Features.BugDiscovery.Caching;
using DataCollection.Application.Features.IssueAnalysis.Rules;
using DataCollection.Application.Models.IssueTracker.Profiles;
using DataCollection.Core.Models.IssueTracker;
using DataCollection.Core.Models.IssueTracker.Responses;
using DataCollection.Infrastructure.Models.GitHub;
using DataCollection.Infrastructure.Serialization;
using GitHub.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace DataCollection.Application.Features.BugDiscovery;

public class GitHubService(
    IGitHubClient gitHubClient,
    ILogger<GitHubService> logger,
    IsDeveloperCriterion isDeveloperCriterion,
    IUserProfileCache userProfileCache
)
{
    public async Task<UserProfile> GetUserProfileAsync(string login, FullRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var repoId = repository.Id.GetValueOrDefault();

        var cachedProfile = await userProfileCache.GetAsync(login, repoId);
        if (cachedProfile != null)
        {
            return cachedProfile;
        }

        var user = await gitHubClient.GetUserAsync(login);
        if (user == null)
            throw new InvalidOperationException($"User {login} not found");

        var userProfile = await CreateUserProfileAsync(login, user, repository);

        await userProfileCache.SetAsync(login, repoId, userProfile);

        return userProfile;
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
        object sdkUser,
        FullRepository repository
    )
    {
        var repoId = repository.Id.GetValueOrDefault();
        var cachedProfile = await userProfileCache.GetAsync(userLogin, repoId);
        if (cachedProfile != null)
        {
            return cachedProfile;
        }

        var userProfile = await CreateUserProfileAsync(userLogin, sdkUser, repository);
        await userProfileCache.SetAsync(userLogin, repoId, userProfile);
        return userProfile;
    }

    private async Task<UserProfile> CreateUserProfileAsync(
        string userLogin,
        object sdkUser,
        FullRepository repository
    )
    {
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
        var finalProfile = basicProfile with
        {
            IsDeveloper = await isDeveloperCriterion.EvaluateAsync(basicProfile),
        };
        logger.LogDebug("User {Profile}", finalProfile);
        return finalProfile;
    }

    public async Task<IssueProfile?> BuildComprehensiveIssueProfileAsync(
        string owner,
        string repoName,
        long issueNumber
    )
    {
        var repository = await gitHubClient.GetRepositoryInfoAsync(owner, repoName);

        var issue = await gitHubClient.GetIssueWithLabelsAsync(owner, repoName, issueNumber);
        if (issue == null)
        {
            logger.LogError($"Issue {issueNumber} not found in repository {owner}/{repoName}");
            return null;
        }

        // Use Refit API client to avoid SDK integer overflow bug
        var issueEvents =
            await gitHubClient.GetIssueEventsAsync(owner, repoName, issueNumber) ?? [];
        var comments = await gitHubClient.GetIssueCommentsAsync(owner, repoName, issueNumber) ?? [];

        // Try to use cached user profile for author first
        var authorProfile = await GetUserProfileAsync(
            issue.User?.Login ?? throw new InvalidOperationException("Issue user is null"),
            issue.User,
            repository
        );

        var (labelEvents, otherEvents) = await ProcessEventsAsync(
            issueEvents,
            authorProfile,
            repository
        );
        var commentEvents = await ProcessCommentsAsync(comments, repository);

        return new IssueProfile
        {
            SdkIssue = issue,
            SdkRepository = repository,
            AuthorProfile = authorProfile,
            RepositoryFullName = repository.FullName ?? $"{owner}/{repoName}",
            IsClosed = issue.State == "closed",
            HasAssociatedPullRequest = issue.PullRequest != null,
            LabelEvents = [.. labelEvents],
            CommentEvents = [.. commentEvents],
            OtherEvents = [.. otherEvents],
        };
    }

    private async Task<(
        List<LabelEventProfile> LabelEvents,
        List<OtherEventProfile> OtherEvents
    )> ProcessEventsAsync(
        IEnumerable<GitHubEvent> issueEvents,
        UserProfile authorProfile,
        FullRepository repository
    )
    {
        var labelEvents = new List<LabelEventProfile>();
        var otherEvents = new List<OtherEventProfile>();

        foreach (var evt in issueEvents)
        {
            if (evt == null)
                continue;

            var userProfile =
                evt.Actor?.Login != null
                    ? await GetUserProfileAsync(evt.Actor.Login, evt.Actor, repository)
                    : authorProfile;

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
                        By = userProfile,
                    }
                );
            }
            else
            {
                otherEvents.Add(
                    new OtherEventProfile
                    {
                        EventType = evt.Event ?? "unknown",
                        By = userProfile,
                        OccurredAt = evt.CreatedAt.ToUniversalTime(),
                        EventDescription = JsonSerializer.Serialize(
                            evt.ExtractDetails(),
                            GitHubAPIJsonContext.Default.GitHubEventDetails
                        ),
                    }
                );
            }
        }

        return (labelEvents, otherEvents);
    }

    private async Task<List<CommentEventProfile>> ProcessCommentsAsync(
        IEnumerable<IssueComment> comments,
        FullRepository repository
    )
    {
        var commentEvents = new List<CommentEventProfile>();
        foreach (var comment in comments)
        {
            if (comment?.User?.Login == null)
                continue;

            var profile = await GetUserProfileAsync(comment.User.Login, comment.User, repository);
            commentEvents.Add(new CommentEventProfile { SdkComment = comment, By = profile });
        }
        return commentEvents;
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
    /// Synthesize a deterministic IssueAnalysisResponse from collected issue profile data.
    /// This simplified deterministic synthesizer only fills rule-based fields and intentionally
    /// leaves subjective fields (IsRealBug, IsDuplicate and their rationale/confidence)
    /// for the LLM to decide. The method purposefully omits heuristic phrase matching here;
    /// the LLM will be asked to evaluate the subjective questions using the rich context.
    /// </summary>
    public async Task<IssueAnalysisResponse> SynthesizeDeterministicIssueAnalysisAsync(
        IssueProfile profile
    )
    {
        // Collect developer usernames: any user who left a comment or performed a label event
        // and who appears to be a developer (IsDeveloper == true OR IsContributor/Collaborator/Member)
        var devUsernames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (profile.CommentEvents?.Length > 0)
        {
            foreach (var c in profile.CommentEvents)
            {
                if (c?.By is not null)
                {
                    if (
                        c.By.IsDeveloper == true
                        || c.By.IsContributor
                        || c.By.IsCollaboratorOrMember == true
                    )
                        devUsernames.Add(c.By.Login);
                }
            }
        }

        if (profile.LabelEvents?.Length > 0)
        {
            foreach (var l in profile.LabelEvents)
            {
                if (l?.By is not null)
                {
                    if (
                        l.By.IsDeveloper == true
                        || l.By.IsContributor
                        || l.By.IsCollaboratorOrMember == true
                    )
                        devUsernames.Add(l.By.Login);
                }
            }
        }

        // Include author if they are a developer
        if (
            profile.AuthorProfile?.IsDeveloper == true
            && !string.IsNullOrWhiteSpace(profile.AuthorProfile.Login)
        )
            devUsernames.Add(profile.AuthorProfile.Login);

        // Determine whether any developer-provided signals exist
        var anyDeveloperCommentsOrLabels =
            (
                profile.CommentEvents?.Any(c =>
                    c?.By is not null
                    && (
                        c.By.IsDeveloper == true
                        || c.By.IsContributor
                        || c.By.IsCollaboratorOrMember == true
                    )
                ) ?? false
            )
            || (
                profile.LabelEvents?.Any(l =>
                    l?.By is not null
                    && (
                        l.By.IsDeveloper == true
                        || l.By.IsContributor
                        || l.By.IsCollaboratorOrMember == true
                    )
                ) ?? false
            );

        var hasDeveloperJudgement = anyDeveloperCommentsOrLabels;

        // Deterministic IsFixed based on associated PR merged state
        bool? isFixed = null;
        bool? isFixedBefore = null;
        if (
            profile.SdkIssue.PullRequest is not null
            && profile.SdkIssue.PullRequest.MergedAt is not null
        )
        {
            isFixed = true;
            var mergedAt = profile.SdkIssue.PullRequest.MergedAt.Value;
            if (profile.SdkIssue.CreatedAt != null)
                isFixedBefore = mergedAt < profile.SdkIssue.CreatedAt;
        }

        // Build minimal deterministic response. Subjective fields are deliberately left empty
        // for the LLM to populate later (IsRealBug, IsDuplicate, associated rationales and confidences).
        var response = new IssueAnalysisResponse
        {
            DeveloperUsernames = devUsernames.ToArray(),
            HasDeveloperJudgement = hasDeveloperJudgement,

            // Subjective fields: intentionally left for LLM
            IsRealBug = null,
            WhetherRealBugRationale = string.Empty,
            ConfidenceInWhetherRealBug = 0.0,

            IsDuplicate = null,
            WhetherDuplicateRationale = string.Empty,
            ConfidenceInWhetherDuplicate = 0.0,

            // Deterministic fields
            IsFixed = isFixed,
            IsFixedBeforeIssueRaised = isFixedBefore,

            IsBugButWontFix = null,
            IsBugButWaitingForAction = null,

            NuanceOrExplanation =
                "Deterministic synthesis: developer presence and PR merged metadata captured. Subjective fields left for LLM.",
            AdditionalNotes = null,
        };

        return await Task.FromResult(response);
    }
}
