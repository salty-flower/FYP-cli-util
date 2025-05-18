using System.Threading.Tasks;

namespace DataCollection.Models.IssueTracker;

public class IssueFixedCriterion : ICriterion<IssueProfile, bool>
{
    public Task<bool> EvaluateAsync(IssueProfile profile) =>
        Task.FromResult(profile is { IsClosed: true, HasAssociatedPullRequest: true });
}