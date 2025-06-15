using ConsoleAppFramework;
using DataCollection.Application.Models.Commands;
using DataCollection.Application.Services;
using DataCollection.Core.Models.Errors;
using DataCollection.Presentation.Cli.Filters;
using Microsoft.Extensions.Logging;

namespace DataCollection.Presentation.Cli.Commands;

[RegisterCommands("batch")]
[ConsoleAppFilter<PathsOptionsFilter>]
public class SimplifiedBatchProcessingCommands
{
    private readonly IBatchProcessingService batchService;
    private readonly ILogger<SimplifiedBatchProcessingCommands> logger;

    public SimplifiedBatchProcessingCommands(
        IBatchProcessingService batchService,
        ILogger<SimplifiedBatchProcessingCommands> logger
    )
    {
        this.batchService = batchService;
        this.logger = logger;
    }

    public async Task<int> ProcessWithOpenAI(
        string issueUrls,
        bool saveResults = true,
        bool useCache = true,
        string batchJobId = null,
        CancellationToken cancellationToken = default
    )
    {
        var issueTasks = ParseIssueUrls(issueUrls);
        var command = new BatchProcessIssuesCommand
        {
            IssueTasks = issueTasks,
            SaveResults = saveResults,
            UseCache = useCache,
            UseOpenAIBatch = true,
            BatchJobId = batchJobId,
        };

        var result = await batchService.ProcessBatchWithOpenAIAsync(command, cancellationToken);

        if (result.IsSuccess)
        {
            logger.LogInformation(
                "Successfully processed {Count} issues using OpenAI Batch API",
                result.Value
            );
            return result.Value;
        }

        LogBatchError(result.Error, "OpenAI batch processing");
        return GetErrorCode(result.Error);
    }

    public async Task<int> ProcessWithParallelism(
        string issueUrls,
        int maxParallelTasks = 10,
        bool saveResults = true,
        bool useCache = true,
        CancellationToken cancellationToken = default
    )
    {
        var issueTasks = ParseIssueUrls(issueUrls);
        var command = new BatchProcessIssuesCommand
        {
            IssueTasks = issueTasks,
            SaveResults = saveResults,
            UseCache = useCache,
            UseOpenAIBatch = false,
            MaxParallelTasks = maxParallelTasks,
        };

        var result = await batchService.ProcessBatchWithParallelismAsync(
            command,
            cancellationToken
        );

        if (result.IsSuccess)
        {
            logger.LogInformation(
                "Successfully processed {Count} issues using parallel processing",
                result.Value
            );
            return result.Value;
        }

        LogBatchError(result.Error, "parallel processing");
        return GetErrorCode(result.Error);
    }

    public async Task<int> ResumeExistingBatch(
        string batchJobId,
        string issueUrls,
        bool saveResults = true,
        bool useCache = true,
        CancellationToken cancellationToken = default
    )
    {
        var issueTasks = ParseIssueUrls(issueUrls);
        var command = new BatchProcessIssuesCommand
        {
            IssueTasks = issueTasks,
            SaveResults = saveResults,
            UseCache = useCache,
            UseOpenAIBatch = true,
        };

        var result = await batchService.ResumeExistingBatchAsync(
            batchJobId,
            command,
            cancellationToken
        );

        if (result.IsSuccess)
        {
            logger.LogInformation(
                "Successfully resumed and processed batch job {BatchJobId}",
                batchJobId
            );
            return result.Value;
        }

        LogBatchError(result.Error, $"batch job resume {batchJobId}");
        return GetErrorCode(result.Error);
    }

    private List<(string? Url, string? Owner, string? Repo, long? Number)> ParseIssueUrls(
        string issueUrls
    )
    {
        if (string.IsNullOrWhiteSpace(issueUrls))
            return new List<(string?, string?, string?, long?)>();

        var urls = issueUrls.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        var issueTasks = new List<(string? Url, string? Owner, string? Repo, long? Number)>();

        foreach (var url in urls)
        {
            if (TryParseGitHubUrl(url, out var owner, out var repo, out var number))
            {
                issueTasks.Add((url, owner, repo, number));
            }
            else
            {
                issueTasks.Add((url, null, null, null));
            }
        }

        return issueTasks;
    }

    private bool TryParseGitHubUrl(string url, out string owner, out string repo, out long number)
    {
        owner = null;
        repo = null;
        number = 0;

        try
        {
            var uri = new Uri(url);
            if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                return false;

            var parts = uri.AbsolutePath.Trim('/').Split('/');
            if (parts.Length >= 4 && parts[2] == "issues" && long.TryParse(parts[3], out number))
            {
                owner = parts[0];
                repo = parts[1];
                return true;
            }
        }
        catch
        {
            // Invalid URL format
        }

        return false;
    }

    private void LogBatchError(BugDiscoveryError error, string operation)
    {
        logger.LogError(
            "Error during {Operation}: {ErrorCode} - {Message}",
            operation,
            error.Code,
            error.Message
        );
    }

    private static int GetErrorCode(BugDiscoveryError error) =>
        error switch
        {
            PdfAnalysisError => 2,
            RepositoryAnalysisError => 3,
            WebSearchError => 4,
            BugDiscoveryConfigurationError => 5,
            BatchProcessingError => 6,
            _ => 1,
        };
}
