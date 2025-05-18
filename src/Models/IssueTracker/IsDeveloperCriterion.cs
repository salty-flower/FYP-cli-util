using System.Threading.Tasks;

namespace DataCollection.Models.IssueTracker;

public class IsDeveloperCriterion : ICriterion<UserProfile, bool>
{
    public Task<bool> EvaluateAsync(UserProfile profile) =>
        Task.FromResult(profile.IsContributor || profile.TotalMergedPullRequests >= 1);
}