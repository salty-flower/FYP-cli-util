using System.Text.Json;
using ConsoleAppFramework;
using DataCollection.Application.Features.IssueProcessing;
using DataCollection.Core.Models.IssueTracker.Responses;
using DataCollection.Presentation.Cli.Filters;
using Microsoft.Extensions.Logging;

namespace DataCollection.Presentation.Cli.Commands;

[RegisterCommands("issue")]
[ConsoleAppFilter<PathsOptionsFilter>]
[ConsoleAppFilter<CredentialOptionsFilter>]
[ConsoleAppFilter<EnsureDBFilter>]
public class IssueCommands(
    ILogger<IssueCommands> logger,
    SingleIssueProcessingService singleIssueService,
    IssueBatchProcessingService batchProcessingService
)
{
    /// <summary>
    /// Analyzes an issue and determines its status.
    /// </summary>
    /// <param name="url">Optional URL to the GitHub issue</param>
    /// <param name="owner">The owner of the repository (if URL not provided)</param>
    /// <param name="repoName">The name of the repository (if URL not provided)</param>
    /// <param name="issueNumber">The number of the issue (if URL not provided)</param>
    /// <param name="saveResults">Whether to save analysis results to disk</param>
    /// <param name="useCache">Whether to use cached analysis results if available</param>
    public async Task DecideStatus(
        string? url,
        string? owner = null,
        string? repoName = null,
        long? issueNumber = null,
        bool saveResults = false,
        bool useCache = true
    )
    {
        // Parse URL if provided
        if (url != null)
        {
            var (parsedOwner, parsedRepo, parsedIssueNumber) = ParseGitHubIssueUrl(url);
            if (parsedOwner == null || parsedRepo == null || !parsedIssueNumber.HasValue)
            {
                logger.LogError("Invalid GitHub issue URL format: {Url}", url);
                return;
            }
            owner = parsedOwner;
            repoName = parsedRepo;
            issueNumber = parsedIssueNumber;
        }

        // Validate required parameters
        if (
            string.IsNullOrWhiteSpace(owner)
            || string.IsNullOrWhiteSpace(repoName)
            || !issueNumber.HasValue
        )
        {
            logger.LogError(
                "Insufficient information: Owner, RepoName, and IssueNumber must be provided if URL is not set."
            );
            return;
        }

        logger.LogInformation(
            "Deciding status for issue: {Owner}/{Repo}#{IssueNumber}",
            owner,
            repoName,
            issueNumber.Value
        );

        try
        {
            await singleIssueService.ProcessIssueAsync(
                owner,
                repoName,
                issueNumber.Value,
                useCache: useCache,
                saveResults: saveResults
            );
        }
        catch (Exception apiEx)
        {
            logger.LogError(
                apiEx,
                "Failed to decide status for issue {Owner}/{Repo}#{IssueNumber}.",
                owner,
                repoName,
                issueNumber.Value
            );
        }
    }

    /// <summary>
    /// Process a batch of issues from a file using OpenAI Batch API
    /// </summary>
    /// <param name="inputFile">Path to a file containing issue URLs or owner/repo/issue combinations, one per line</param>
    /// <param name="saveResults">Whether to save analysis results to disk</param>
    /// <param name="useCache">Whether to use cached results if available</param>
    /// <param name="useBatchApi">Whether to use OpenAI Batch API for processing (default true for cost savings)</param>
    /// <param name="batchJobId">Optional existing OpenAI batch job ID to resume/check status instead of creating new batch</param>
    /// <param name="outputPath">Optional path to export results as JSONL (one IssueAnalysisResponse per line)</param>
    public async Task ProcessBatch(
        string inputFile,
        bool saveResults = true,
        bool useCache = true,
        bool useBatchApi = true,
        string? batchJobId = null,
        string? outputPath = null,
        CancellationToken cancellation = default
    )
    {
        if (!File.Exists(inputFile))
        {
            logger.LogError("Input file not found: {InputFile}", inputFile);
            return;
        }

        var lines = await File.ReadAllLinesAsync(inputFile);
        logger.LogInformation(
            "Processing {Count} issues from {InputFile}",
            lines.Length,
            inputFile
        );

        // Parse and collect all valid issues
        var issueTasks = ParseIssueLines(lines);

        logger.LogInformation(
            "Found {ValidCount} valid issues out of {TotalCount}",
            issueTasks.Count,
            lines.Length
        );

        // De-duplicate issues
        issueTasks = [.. issueTasks.Distinct()];

        // Delegate to appropriate batch processing service
        List<IssueAnalysisResult> results;
        if (useBatchApi)
            results = await batchProcessingService.ProcessBatchWithOpenAIAsync(
                issueTasks,
                saveResults,
                useCache,
                batchJobId,
                cancellation
            );
        else
        {
            if (!string.IsNullOrEmpty(batchJobId))
                logger.LogWarning(
                    "Batch job ID provided but useBatchApi is false. Ignoring batch job ID and using parallel processing."
                );

            results = await batchProcessingService.ProcessBatchWithParallelismAsync(
                issueTasks,
                saveResults,
                useCache,
                cancellation
            );
        }

        // Export results to JSONL if output path is provided
        if (!string.IsNullOrEmpty(outputPath))
        {
            await ExportResultsToJsonl(results, outputPath);
            logger.LogInformation(
                "Exported {Count} results to {OutputPath}",
                results.Count,
                outputPath
            );
        }
    }

    /// <summary>
    /// Prepares batch request payloads without calling the OpenAI Batch API.
    /// </summary>
    /// <param name="inputFile">Path to a file containing issue URLs or owner/repo/issue combinations.</param>
    /// <param name="outputPath">Destination JSONL file to store the batch request payloads.</param>
    /// <param name="useCache">Whether to skip issues that already have cached objective data.</param>
    public async Task PrepareBatchRequests(
        string inputFile,
        string outputPath,
        bool useCache = false,
        CancellationToken cancellation = default
    )
    {
        if (!File.Exists(inputFile))
        {
            logger.LogError("Input file not found: {InputFile}", inputFile);
            return;
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            logger.LogError("Output path must be provided.");
            return;
        }

        var lines = await File.ReadAllLinesAsync(inputFile, cancellation);
        logger.LogInformation(
            "Preparing batch requests for {Count} entries from {InputFile}",
            lines.Length,
            inputFile
        );

        var issueTasks = ParseIssueLines(lines);
        issueTasks = [.. issueTasks.Distinct()];

        if (issueTasks.Count == 0)
        {
            logger.LogWarning("No valid issues were found in {InputFile}", inputFile);
            return;
        }

        try
        {
            var records = await batchProcessingService.PrepareBatchRequestsAsync(
                issueTasks,
                useCache,
                cancellation
            );

            if (records.Count == 0)
            {
                logger.LogWarning(
                    "No batch request payloads were produced. Check caching settings or input data."
                );
                return;
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var writer = new StreamWriter(outputPath);
            foreach (var record in records)
            {
                var json = JsonSerializer.Serialize(
                    record,
                    IssueBatchPreparationExportJsonContext.Default.IssueBatchPreparationRecord
                );
                await writer.WriteLineAsync(json);
            }

            logger.LogInformation(
                "Wrote {Count} batch request payloads to {OutputPath}",
                records.Count,
                outputPath
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to prepare batch requests from {InputFile}", inputFile);
        }
    }

    /// <summary>
    /// Parses GitHub issue URL and extracts owner, repo, and issue number
    /// </summary>
    private (string? Owner, string? Repo, long? IssueNumber) ParseGitHubIssueUrl(string url)
    {
        var isUri = Uri.TryCreate(url, UriKind.Absolute, out var uri);
        if (!isUri || uri == null)
            return (null, null, null);

        if (uri.Segments.Length < 5)
        {
            logger.LogError(
                "Invalid GitHub issue URL format: {Url}. Expected at least 5 segments.",
                url
            );
            return (null, null, null);
        }

        var owner = uri.Segments[1].TrimEnd('/');
        var repo = uri.Segments[2].TrimEnd('/');
        var shouldBeIssueNumber = uri.Segments[4].TrimEnd('/');

        if (!long.TryParse(shouldBeIssueNumber, out var issueNumber))
        {
            logger.LogError(
                "Could not parse issue number from URL segment: {Segment}",
                shouldBeIssueNumber
            );
            return (null, null, null);
        }

        return (owner, repo, issueNumber);
    }

    /// <summary>
    /// Parses lines from input file and extracts issue information
    /// </summary>
    private List<(string? Url, string? Owner, string? Repo, long? Number)> ParseIssueLines(
        string[] lines
    )
    {
        var issueTasks = new List<(string? Url, string? Owner, string? Repo, long? Number)>();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith('#'))
                continue;

            if (trimmedLine.StartsWith("http"))
                // Process as URL
                issueTasks.Add((trimmedLine, null, null, null));
            else
            {
                // Process as owner/repo/issue format
                var parts = trimmedLine.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && long.TryParse(parts[2], out var issueNum))
                    issueTasks.Add((null, parts[0], parts[1], issueNum));
                else
                    logger.LogWarning("Invalid issue format: {Line}", trimmedLine);
            }
        }

        return issueTasks;
    }

    /// <summary>
    /// Exports analysis results to a JSONL file
    /// </summary>
    private static async Task ExportResultsToJsonl(
        List<IssueAnalysisResult> results,
        string outputPath
    )
    {
        using var writer = new StreamWriter(outputPath);
        foreach (var result in results)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(
                result,
                IssueExportJsonContext.Default.IssueAnalysisResult
            );
            await writer.WriteLineAsync(json);
        }
    }
}
