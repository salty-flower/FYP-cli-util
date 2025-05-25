using System.Collections.Generic;
using System.Threading.Tasks;

namespace DataCollection.Models.IssueTracker.Criteria;

public interface IBatchCriterion<TProfile, TOutcome> : ICriterion<TProfile, TOutcome>
{
    Task<Dictionary<string, TOutcome>> EvaluateBatchAsync(Dictionary<string, TProfile> profiles);

    Task<Dictionary<string, TOutcome>> ResumeBatchAsync(string batchJobId);
}

public interface ICriterion<in TProfile, TOutcome>
{
    Task<TOutcome> EvaluateAsync(TProfile profile);
}
