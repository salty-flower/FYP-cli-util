using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataCollection.Application.Features.BugDiscovery.Caching;
using DataCollection.Application.Features.IssueAnalysis.Rules;
using DataCollection.Application.Models.IssueTracker.Profiles;
using DataCollection.Core.Models.IssueTracker;
using DataCollection.Core.Models.IssueTracker.Responses;
using DataCollection.Infrastructure.Clients.IssueTrackers;
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
    public async Task<UserProfile> GetUserProfileAsync(
        string login,
        FullRepository repository,
        CancellationToken cancellationToken = default
    )
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

        var userProfile = await CreateUserProfileAsync(login, user, repository, cancellationToken);

        await userProfileCache.SetAsync(login, repoId, userProfile);

        return userProfile;
    }

    /// <summary>
    /// Gets a user's profile in the context of a specific repository
    /// </summary>
    /// <param name="userLogin">User login</param>
    /// <param name="sdkUser">User information from SDK</param>
    /// <param name="repository">Repository context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>User profile with repository-specific metrics</returns>
    public async Task<UserProfile> GetUserProfileAsync(
        string userLogin,
        object sdkUser,
        FullRepository repository,
        CancellationToken cancellationToken = default
    )
    {
        var repoId = repository.Id.GetValueOrDefault();
        var cachedProfile = await userProfileCache.GetAsync(userLogin, repoId);
        if (cachedProfile != null)
        {
            return cachedProfile;
        }

        var userProfile = await CreateUserProfileAsync(
            userLogin,
            sdkUser,
            repository,
            cancellationToken
        );
        await userProfileCache.SetAsync(userLogin, repoId, userProfile);
        return userProfile;
    }

    private async Task<UserProfile> CreateUserProfileAsync(
        string userLogin,
        object sdkUser,
        FullRepository repository,
        CancellationToken cancellationToken = default
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
        long issueNumber,
        CancellationToken cancellationToken = default
    )
    {
        var repository = await gitHubClient.GetRepositoryInfoAsync(
            owner,
            repoName,
            cancellationToken
        );

        var issue = await gitHubClient.GetIssueWithLabelsAsync(
            owner,
            repoName,
            issueNumber,
            cancellationToken
        );
        if (issue == null)
        {
            logger.LogError($"Issue {issueNumber} not found in repository {owner}/{repoName}");
            return null;
        }

        // Use Refit API client to avoid SDK integer overflow bug
        var issueEvents =
            await gitHubClient.GetIssueEventsAsync(owner, repoName, issueNumber, cancellationToken)
            ?? [];
        var comments =
            await gitHubClient.GetIssueCommentsAsync(
                owner,
                repoName,
                issueNumber,
                cancellationToken
            ) ?? [];

        // Try to use cached user profile for author first
        var authorProfile = await GetUserProfileAsync(
            issue.User?.Login ?? throw new InvalidOperationException("Issue user is null"),
            issue.User,
            repository,
            cancellationToken
        );

        var timelineEvents =
            await gitHubClient.GetIssueTimelineAsync(
                owner,
                repoName,
                issueNumber,
                cancellationToken
            ) ?? [];

        var (labelEvents, otherEvents) = await ProcessEventsAsync(
            issueEvents,
            authorProfile,
            repository,
            cancellationToken
        );

        if (timelineEvents.Count > 0)
        {
            var timelineProfiles = await ProcessTimelineEventsAsync(
                timelineEvents,
                authorProfile,
                repository,
                cancellationToken
            );
            otherEvents.AddRange(timelineProfiles);
        }
        var commentEvents = await ProcessCommentsAsync(comments, repository, cancellationToken);

        var commitBriefings = await BuildCommitBriefingsAsync(
            owner,
            repoName,
            otherEvents,
            cancellationToken
        );

        var pullRequestBriefing = await BuildPullRequestBriefingAsync(
            owner,
            repoName,
            issue,
            cancellationToken
        );

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
            AssociatedCommitBriefings = [.. commitBriefings],
            AssociatedPullRequest = pullRequestBriefing,
        };
    }

    private async Task<(
        List<LabelEventProfile> LabelEvents,
        List<OtherEventProfile> OtherEvents
    )> ProcessEventsAsync(
        IEnumerable<GitHubEvent> issueEvents,
        UserProfile authorProfile,
        FullRepository repository,
        CancellationToken cancellationToken = default
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
                    ? await GetUserProfileAsync(
                        evt.Actor.Login,
                        evt.Actor,
                        repository,
                        cancellationToken
                    )
                    : authorProfile;

            if (
                evt is not null
                && evt.Event?.ToLowerInvariant() is "labeled" or "unlabeled"
                && evt.Label != null
            )
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
                        CommitId = string.IsNullOrWhiteSpace(evt.CommitId) ? null : evt.CommitId,
                        CommitUrl = string.IsNullOrWhiteSpace(evt.CommitUrl) ? null : evt.CommitUrl,
                    }
                );
            }
        }

        return (labelEvents, otherEvents);
    }

    private async Task<List<OtherEventProfile>> ProcessTimelineEventsAsync(
        IEnumerable<GitHubTimelineEvent> timelineEvents,
        UserProfile authorProfile,
        FullRepository repository,
        CancellationToken cancellationToken = default
    )
    {
        var otherEvents = new List<OtherEventProfile>();

        foreach (var evt in timelineEvents)
        {
            if (evt is null)
            {
                continue;
            }

            var pullRequest = evt.Source?.Issue?.PullRequest;

            var actorProfile = authorProfile;
            if (!string.IsNullOrWhiteSpace(evt.Actor?.Login))
            {
                actorProfile = await GetUserProfileAsync(
                    evt.Actor.Login,
                    evt.Actor,
                    repository,
                    cancellationToken
                );
            }

            otherEvents.Add(
                new OtherEventProfile
                {
                    EventType = evt.Event ?? "unknown",
                    By = actorProfile,
                    OccurredAt = evt.CreatedAt.ToUniversalTime(),
                    EventDescription = JsonSerializer.Serialize(
                        evt.ExtractDetails(),
                        GitHubAPIJsonContext.Default.GitHubTimelineEventDetails
                    ),
                    CommitId = string.IsNullOrWhiteSpace(evt.CommitId) ? null : evt.CommitId,
                    CommitUrl = string.IsNullOrWhiteSpace(evt.CommitUrl) ? null : evt.CommitUrl,
                    PullRequestUrl = pullRequest?.HtmlUrl,
                    PullRequestMergedAt = pullRequest?.MergedAt,
                }
            );
        }

        return otherEvents;
    }

    private async Task<List<CommentEventProfile>> ProcessCommentsAsync(
        IEnumerable<GitHubIssueComment> comments,
        FullRepository repository,
        CancellationToken cancellationToken = default
    )
    {
        var commentEvents = new List<CommentEventProfile>();
        foreach (var comment in comments)
        {
            if (comment?.User?.Login == null)
                continue;

            var profile = await GetUserProfileAsync(
                comment.User.Login,
                comment.User,
                repository,
                cancellationToken
            );
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
    /// Synthesize a deterministic analysis from collected issue profile data.
    /// This simplified deterministic synthesizer only fills rule-based fields and intentionally
    /// leaves subjective fields (IsRealBug, IsDuplicate and their rationale/confidence)
    /// for the LLM to decide. The method purposefully omits heuristic phrase matching here;
    /// the LLM will be asked to evaluate the subjective questions using the rich context.
    /// </summary>
    public DeterministicIssueAnalysis SynthesizeDeterministicIssueAnalysis(IssueProfile profile)
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
        var anyDeveloperComments = (
            profile.CommentEvents?.Any(c =>
                c?.By is not null
                && (
                    c.By.IsDeveloper == true
                    || c.By.IsContributor
                    || c.By.IsCollaboratorOrMember == true
                )
            ) ?? false
        );

        var hasDeveloperJudgement = anyDeveloperComments;

        // Deterministic IsFixed based on associated PR merged state or commits referencing the issue
        bool? isFixed = null;
        bool? isFixedBefore = null;
        var closedAt = profile.SdkIssue.ClosedAt;
        if (
            profile.SdkIssue.PullRequest is not null
            && profile.SdkIssue.PullRequest.MergedAt is not null
        )
        {
            isFixed = true;
            var mergedAt = profile.SdkIssue.PullRequest.MergedAt.Value;
            isFixedBefore = mergedAt < profile.SdkIssue.CreatedAt;
        }
        else if (profile.IsClosed)
        {
            var fixReferenceEvents =
                profile.OtherEvents?.Where(IsLikelyFixingCommitEvent)
                ?? Enumerable.Empty<OtherEventProfile>();

            fixReferenceEvents = fixReferenceEvents.Where(evt =>
                evt.OccurredAt >= profile.SdkIssue.CreatedAt.AddMinutes(-5)
            );

            if (closedAt.HasValue)
            {
                var latestRelevant = closedAt.Value.AddDays(30);
                fixReferenceEvents = fixReferenceEvents.Where(evt =>
                    evt.OccurredAt <= latestRelevant
                );
            }

            var relevantFixEvent = fixReferenceEvents
                .OrderByDescending(e => e.OccurredAt)
                .FirstOrDefault();

            if (relevantFixEvent != null)
            {
                isFixed = true;
                if (closedAt.HasValue)
                {
                    isFixedBefore = relevantFixEvent.OccurredAt < profile.SdkIssue.CreatedAt;
                }
                else
                {
                    isFixedBefore = null;
                }
            }
        }

        // Build minimal deterministic response. Subjective fields are deliberately left empty
        // for the LLM to populate later (IsRealBug, IsDuplicate, associated rationales and confidences).
        return new DeterministicIssueAnalysis
        {
            DeveloperUsernames = [.. devUsernames],
            HasDeveloperJudgement = hasDeveloperJudgement,
            IsFixed = isFixed,
            NuanceOrExplanation =
                "Deterministic synthesis: developer presence and PR merged metadata captured. Subjective fields left for LLM.",
            AdditionalNotes = null,
        };
    }

    private static bool IsLikelyFixingCommitEvent(OtherEventProfile evt)
    {
        if (evt.EventType is null)
        {
            return false;
        }

        if (string.Equals(evt.EventType, "cross-referenced", StringComparison.OrdinalIgnoreCase))
        {
            return evt.PullRequestMergedAt.HasValue;
        }

        static bool IsCommitEventType(string eventType) =>
            eventType.Equals("referenced", StringComparison.OrdinalIgnoreCase)
            || eventType.Equals("merged", StringComparison.OrdinalIgnoreCase)
            || eventType.Equals("closed", StringComparison.OrdinalIgnoreCase);

        if (!IsCommitEventType(evt.EventType))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(evt.CommitId))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(evt.CommitUrl))
        {
            return false;
        }

        return evt.CommitUrl.Contains("/commit/", StringComparison.OrdinalIgnoreCase)
            || evt.CommitUrl.Contains("/pull/", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyList<CommitBriefing>> BuildCommitBriefingsAsync(
        string owner,
        string repoName,
        IReadOnlyCollection<OtherEventProfile> otherEvents,
        CancellationToken cancellationToken
    )
    {
        if (otherEvents.Count == 0)
        {
            return Array.Empty<CommitBriefing>();
        }

        var commitIds = otherEvents
            .Where(evt => !string.IsNullOrWhiteSpace(evt.CommitId))
            .Select(evt => evt.CommitId!.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (commitIds.Length == 0)
        {
            return Array.Empty<CommitBriefing>();
        }

        var fetchTasks = commitIds
            .Select(async sha =>
                (
                    Sha: sha,
                    Commit: await gitHubClient.GetCommitAsync(
                        owner,
                        repoName,
                        sha,
                        cancellationToken
                    )
                )
            )
            .ToArray();

        await Task.WhenAll(fetchTasks);

        var results = new List<CommitBriefing>(fetchTasks.Length);
        foreach (var task in fetchTasks)
        {
            var (sha, commit) = task.Result;
            if (commit == null)
            {
                continue;
            }

            results.Add(CreateCommitBriefing(owner, repoName, sha, commit));
        }

        return results
            .OrderByDescending(briefing => briefing.AuthoredDate ?? DateTimeOffset.MinValue)
            .ToArray();
    }

    private static CommitBriefing CreateCommitBriefing(
        string owner,
        string repoName,
        string sha,
        GitHubCommit commit
    )
    {
        var (headline, body) = SplitCommitMessage(commit.Commit?.Message);
        var htmlUrl = !string.IsNullOrWhiteSpace(commit.HtmlUrl)
            ? commit.HtmlUrl
            : $"https://github.com/{owner}/{repoName}/commit/{sha}";

        CommitAuthorBriefing? author = null;
        var commitAuthor = commit.Commit?.Author;
        if (commitAuthor != null || commit.Author != null || commit.Committer != null)
        {
            author = new CommitAuthorBriefing
            {
                Login = commit.Author?.Login ?? commit.Committer?.Login,
                Name = commitAuthor?.Name ?? commit.Author?.Login ?? commit.Committer?.Login,
                Email = commitAuthor?.Email,
                Date = commitAuthor?.Date,
                HtmlUrl = commit.Author?.HtmlUrl ?? commit.Committer?.HtmlUrl,
            };
        }

        var stats =
            commit.Stats != null
                ? new CommitDiffStatBriefing
                {
                    Additions = commit.Stats.Additions,
                    Deletions = commit.Stats.Deletions,
                    TotalChanges = commit.Stats.Total,
                }
                : null;

        var files =
            commit
                .Files?.Where(f => !string.IsNullOrWhiteSpace(f.Filename))
                .Select(f => new CommitFileBriefing
                {
                    FileName = f.Filename!,
                    Status = f.Status,
                    Additions = f.Additions,
                    Deletions = f.Deletions,
                    Changes = f.Changes,
                })
                .ToArray() ?? Array.Empty<CommitFileBriefing>();

        return new CommitBriefing
        {
            Sha = sha,
            HtmlUrl = htmlUrl,
            MessageHeadline = headline,
            MessageBody = body,
            Author = author,
            Stats = stats,
            Files = files,
        };
    }

    private async Task<PullRequestBriefing?> BuildPullRequestBriefingAsync(
        string owner,
        string repoName,
        GitHubIssue issue,
        CancellationToken cancellationToken
    )
    {
        if (issue.PullRequest?.HtmlUrl is null)
        {
            return null;
        }

        if (!TryExtractPullRequestNumber(issue.PullRequest.HtmlUrl, out var pullNumber))
        {
            return null;
        }

        var pullRequest = await gitHubClient.GetPullRequestAsync(
            owner,
            repoName,
            pullNumber,
            cancellationToken
        );

        if (pullRequest == null)
        {
            return null;
        }

        var pullRequestFiles = await gitHubClient.GetPullRequestFilesAsync(
            owner,
            repoName,
            pullNumber,
            cancellationToken
        );

        var fileBriefings =
            pullRequestFiles
                ?.Where(f => !string.IsNullOrWhiteSpace(f.Filename))
                .Select(f => new PullRequestFileBriefing
                {
                    FileName = f.Filename!,
                    Status = f.Status,
                    Additions = f.Additions,
                    Deletions = f.Deletions,
                    Changes = f.Changes,
                })
                .ToArray() ?? Array.Empty<PullRequestFileBriefing>();

        return new PullRequestBriefing
        {
            Number = pullRequest.Number,
            Title = pullRequest.Title,
            Body = pullRequest.Body,
            State = pullRequest.State,
            HtmlUrl = pullRequest.HtmlUrl ?? issue.PullRequest.HtmlUrl,
            AuthorLogin = pullRequest.User?.Login,
            AuthorName = pullRequest.User?.Login,
            CreatedAt = pullRequest.CreatedAt,
            MergedAt = pullRequest.MergedAt ?? issue.PullRequest.MergedAt,
            ClosedAt = pullRequest.ClosedAt,
            Additions = pullRequest.Additions,
            Deletions = pullRequest.Deletions,
            ChangedFiles = pullRequest.ChangedFiles ?? fileBriefings.Length,
            Files = fileBriefings,
        };
    }

    private static bool TryExtractPullRequestNumber(string htmlUrl, out int pullNumber)
    {
        pullNumber = 0;

        if (!Uri.TryCreate(htmlUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var pullIndex = Array.IndexOf(segments, "pull");
        if (pullIndex >= 0 && pullIndex + 1 < segments.Length)
        {
            return int.TryParse(segments[pullIndex + 1], out pullNumber);
        }

        return false;
    }

    private static (string? Headline, string? Body) SplitCommitMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return (null, null);
        }

        var normalized = message.Replace("\r\n", "\n", StringComparison.Ordinal);
        var parts = normalized.Split('\n', 2, StringSplitOptions.TrimEntries);
        var headline = parts.Length > 0 ? parts[0] : null;
        var body = parts.Length > 1 ? parts[1]?.Trim() : null;

        if (string.IsNullOrWhiteSpace(headline))
        {
            headline = null;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            body = null;
        }

        return (headline, body);
    }
}
