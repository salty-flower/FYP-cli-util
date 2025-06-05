using DataCollection.Application.Models.IssueTracker.Profiles;

namespace DataCollection.Application.Features.IssueAnalysis.Rules;

public class IssueFixedCriterion : ICriterion<IssueProfile, bool>
{
    public Task<bool> EvaluateAsync(IssueProfile profile) =>
        Task.FromResult(profile is { IsClosed: true, HasAssociatedPullRequest: true });
}

public class IsDeveloperCriterion : ICriterion<UserProfile, bool>
{
    public Task<bool> EvaluateAsync(UserProfile profile) =>
        Task.FromResult(profile.IsContributor || profile.TotalMergedPullRequests >= 1);
}
