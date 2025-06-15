using CSharpFunctionalExtensions;
using DataCollection.Application.Features.IssueProcessing;
using DataCollection.Application.Models.Commands;
using DataCollection.Core.Models.Errors;
using Microsoft.Extensions.Logging;

namespace DataCollection.Application.Services;

public class PureBatchProcessingService : IBatchProcessingService
{
    private readonly IssueBatchProcessingService batchProcessor;
    private readonly ILogger<PureBatchProcessingService> logger;

    public PureBatchProcessingService(
        IssueBatchProcessingService batchProcessor,
        ILogger<PureBatchProcessingService> logger
    )
    {
        this.batchProcessor = batchProcessor;
        this.logger = logger;
    }

    public async Task<Result<int, BugDiscoveryError>> ProcessBatchWithOpenAIAsync(
        BatchProcessIssuesCommand command,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            logger.LogInformation(
                "Processing {Count} issues with OpenAI Batch API",
                command.IssueTasks.Count
            );

            await batchProcessor.ProcessBatchWithOpenAIAsync(
                command.IssueTasks,
                command.SaveResults,
                command.UseCache,
                command.BatchJobId
            );

            logger.LogInformation(
                "Successfully processed {Count} issues with OpenAI Batch API",
                command.IssueTasks.Count
            );
            return Result.Success<int, BugDiscoveryError>(command.IssueTasks.Count);
        }
        catch (OperationCanceledException)
        {
            return new BatchProcessingError(
                command.BatchJobId ?? "unknown",
                "Operation was cancelled"
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing batch with OpenAI API");
            return new BatchProcessingError(command.BatchJobId ?? "unknown", ex.Message);
        }
    }

    public async Task<Result<int, BugDiscoveryError>> ProcessBatchWithParallelismAsync(
        BatchProcessIssuesCommand command,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            logger.LogInformation(
                "Processing {Count} issues with parallel processing",
                command.IssueTasks.Count
            );

            await batchProcessor.ProcessBatchWithParallelismAsync(
                command.IssueTasks,
                command.SaveResults,
                command.UseCache,
                command.MaxParallelTasks
            );

            logger.LogInformation(
                "Successfully processed {Count} issues with parallel processing",
                command.IssueTasks.Count
            );
            return Result.Success<int, BugDiscoveryError>(command.IssueTasks.Count);
        }
        catch (OperationCanceledException)
        {
            return new BatchProcessingError("parallel", "Operation was cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing batch with parallel processing");
            return new BatchProcessingError("parallel", ex.Message);
        }
    }

    public async Task<Result<int, BugDiscoveryError>> ResumeExistingBatchAsync(
        string batchJobId,
        BatchProcessIssuesCommand command,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            logger.LogInformation("Resuming existing batch job: {BatchJobId}", batchJobId);

            await batchProcessor.ProcessBatchWithOpenAIAsync(
                command.IssueTasks,
                command.SaveResults,
                command.UseCache,
                batchJobId
            );

            logger.LogInformation("Successfully resumed batch job: {BatchJobId}", batchJobId);
            return Result.Success<int, BugDiscoveryError>(command.IssueTasks.Count);
        }
        catch (OperationCanceledException)
        {
            return new BatchProcessingError(batchJobId, "Operation was cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error resuming batch job {BatchJobId}", batchJobId);
            return new BatchProcessingError(batchJobId, ex.Message);
        }
    }
}
