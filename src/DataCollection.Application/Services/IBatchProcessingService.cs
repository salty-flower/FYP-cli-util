using CSharpFunctionalExtensions;
using DataCollection.Application.Models.Commands;
using DataCollection.Core.Models.Errors;

namespace DataCollection.Application.Services;

public interface IBatchProcessingService
{
    Task<Result<int, BugDiscoveryError>> ProcessBatchWithOpenAIAsync(
        BatchProcessIssuesCommand command,
        CancellationToken cancellationToken = default
    );

    Task<Result<int, BugDiscoveryError>> ProcessBatchWithParallelismAsync(
        BatchProcessIssuesCommand command,
        CancellationToken cancellationToken = default
    );

    Task<Result<int, BugDiscoveryError>> ResumeExistingBatchAsync(
        string batchJobId,
        BatchProcessIssuesCommand command,
        CancellationToken cancellationToken = default
    );
}
