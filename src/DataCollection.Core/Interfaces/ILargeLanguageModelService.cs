using CSharpFunctionalExtensions;
using DataCollection.Core.Models.Errors;

namespace DataCollection.Core.Interfaces;

public interface ILargeLanguageModelService<TProfile, TOutcome>
{
    Task<Result<TOutcome, IssueAnalysisError>> EvaluateAsync(TProfile profile, string modelName);

    Task<Result<Dictionary<string, TOutcome>, IssueAnalysisError>> EvaluateBatchAsync(
        Dictionary<string, TProfile> profiles,
        string modelName
    );

    Task<Result<Dictionary<string, TOutcome>, IssueAnalysisError>> ResumeBatchAsync(
        string batchJobId
    );
}
