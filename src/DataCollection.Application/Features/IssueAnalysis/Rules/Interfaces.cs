namespace DataCollection.Application.Features.IssueAnalysis.Rules;

public interface IBatchCriterion<TProfile, TOutcome> : ICriterion<TProfile, TOutcome>
{
    Task<Dictionary<string, TOutcome>> EvaluateBatchAsync(
        Dictionary<string, TProfile> profiles,
        CancellationToken cancellationToken = default
    );

    Task<Dictionary<string, TOutcome>> ResumeBatchAsync(
        string batchJobId,
        CancellationToken cancellationToken = default
    );
}

public interface ICriterion<in TProfile, TOutcome>
{
    Task<TOutcome> EvaluateAsync(TProfile profile);
}
