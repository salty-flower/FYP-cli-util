using DataCollection.Application.Models.IssueTracker.Profiles;

namespace DataCollection.Application.Features.IssueAnalysis.Rules;

public class IssueFixedCriterion : ICriterion<IssueProfile, bool>
{
    public Task<bool> EvaluateAsync(IssueProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var hasMergedPullRequest =
            profile.AssociatedPullRequest?.MergedAt is not null
            || profile.OtherEvents.Any(evt => evt.PullRequestMergedAt.HasValue);

        var hasRepositoryCommit = profile.AssociatedCommitBriefings.Length > 0;

        var isFixed = profile.IsClosed && (hasMergedPullRequest || hasRepositoryCommit);

        return Task.FromResult(isFixed);
    }
}

public class IsDeveloperCriterion : ICriterion<UserProfile, bool>
{
    public Task<bool> EvaluateAsync(UserProfile profile) =>
        Task.FromResult(profile.IsContributor || profile.TotalMergedPullRequests >= 1);
}
