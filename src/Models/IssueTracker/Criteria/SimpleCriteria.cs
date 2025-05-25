using System.Threading.Tasks;
using DataCollection.Models.IssueTracker.Profiles;

namespace DataCollection.Models.IssueTracker.Criteria;

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
