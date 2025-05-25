using System.Threading.Tasks;

namespace DataCollection.Models.IssueTracker;

public interface ICriterion<in TProfile, TOutcome>
{
    Task<TOutcome> EvaluateAsync(TProfile profile);
}
