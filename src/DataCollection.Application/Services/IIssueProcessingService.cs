using CSharpFunctionalExtensions;
using DataCollection.Application.Models.Commands;
using DataCollection.Core.Models.Errors;

namespace DataCollection.Application.Services;

public interface IIssueProcessingService
{
    Task<Result<IssueInfo, IssueProcessingError>> ParseIssueFromUrlAsync(
        string url,
        CancellationToken cancellationToken = default
    );

    Task<Result<bool, IssueProcessingError>> ProcessSingleIssueAsync(
        DecideIssueStatusCommand command,
        CancellationToken cancellationToken = default
    );

    Task<Result<IReadOnlyList<IssueInfo>, IssueProcessingError>> ParseIssuesFromFileAsync(
        string filePath,
        CancellationToken cancellationToken = default
    );

    Task<Result<bool, IssueProcessingError>> ProcessIssueBatchAsync(
        ProcessIssueBatchCommand command,
        CancellationToken cancellationToken = default
    );
}
