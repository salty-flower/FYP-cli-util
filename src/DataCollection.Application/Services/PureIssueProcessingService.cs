using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;
using DataCollection.Application.Features.IssueProcessing;
using DataCollection.Application.Models.Commands;
using DataCollection.Core.Models.Errors;
using Microsoft.Extensions.Logging;

namespace DataCollection.Application.Services;

public partial class PureIssueProcessingService : IIssueProcessingService
{
    private readonly SingleIssueProcessingService singleIssueService;
    private readonly IssueBatchProcessingService batchProcessingService;
    private readonly ILogger<PureIssueProcessingService> logger;

    public PureIssueProcessingService(
        SingleIssueProcessingService singleIssueService,
        IssueBatchProcessingService batchProcessingService,
        ILogger<PureIssueProcessingService> logger
    )
    {
        this.singleIssueService = singleIssueService;
        this.batchProcessingService = batchProcessingService;
        this.logger = logger;
    }

    [GeneratedRegex(@"github\.com/([^/]+)/([^/]+)/issues/(\d+)", RegexOptions.Compiled)]
    private static partial Regex GitHubUrlPattern();

    public Task<Result<IssueInfo, IssueProcessingError>> ParseIssueFromUrlAsync(
        string url,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return Task.FromResult(
                    Result.Failure<IssueInfo, IssueProcessingError>(new InvalidUrlError(url))
                );

            var match = GitHubUrlPattern().Match(url);
            if (!match.Success)
                return Task.FromResult(
                    Result.Failure<IssueInfo, IssueProcessingError>(new InvalidUrlError(url))
                );

            var owner = match.Groups[1].Value;
            var repo = match.Groups[2].Value;
            var issueNumberText = match.Groups[3].Value;

            if (!long.TryParse(issueNumberText, out var issueNumber))
                return Task.FromResult(
                    Result.Failure<IssueInfo, IssueProcessingError>(new InvalidUrlError(url))
                );

            var issueInfo = new IssueInfo(owner, repo, issueNumber);
            return Task.FromResult(Result.Success<IssueInfo, IssueProcessingError>(issueInfo));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse URL {Url}", url);
            return Task.FromResult(
                Result.Failure<IssueInfo, IssueProcessingError>(new InvalidUrlError(url))
            );
        }
    }

    public async Task<Result<bool, IssueProcessingError>> ProcessSingleIssueAsync(
        DecideIssueStatusCommand command,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            string? owner,
                repoName;
            long? issueNumber;

            // Parse URL if provided
            if (!string.IsNullOrWhiteSpace(command.Url))
            {
                var parseResult = await ParseIssueFromUrlAsync(command.Url, cancellationToken);
                if (parseResult.IsFailure)
                    return parseResult.Error;

                var issueInfo = parseResult.Value;
                owner = issueInfo.Owner;
                repoName = issueInfo.Repo;
                issueNumber = issueInfo.IssueNumber;
            }
            else
            {
                owner = command.Owner;
                repoName = command.RepoName;
                issueNumber = command.IssueNumber;
            }

            // Validate required parameters
            if (
                string.IsNullOrWhiteSpace(owner)
                || string.IsNullOrWhiteSpace(repoName)
                || !issueNumber.HasValue
            )
            {
                return new MissingParametersError(
                    "Owner, RepoName, and IssueNumber must be provided"
                );
            }

            await singleIssueService.ProcessIssueAsync(
                owner,
                repoName,
                issueNumber.Value,
                useCache: command.UseCache,
                saveResults: command.SaveResults
            );

            return Result.Success<bool, IssueProcessingError>(true);
        }
        catch (OperationCanceledException)
        {
            return new IssueProcessingFailedError(
                command.Owner ?? "unknown",
                command.RepoName ?? "unknown",
                command.IssueNumber ?? 0,
                "Operation was cancelled"
            );
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to process issue {Owner}/{Repo}#{IssueNumber}",
                command.Owner,
                command.RepoName,
                command.IssueNumber
            );
            return new IssueProcessingFailedError(
                command.Owner ?? "unknown",
                command.RepoName ?? "unknown",
                command.IssueNumber ?? 0,
                ex.Message
            );
        }
    }

    public async Task<
        Result<IReadOnlyList<IssueInfo>, IssueProcessingError>
    > ParseIssuesFromFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(filePath))
                return new BatchProcessingFailedError(0, $"Input file not found: {filePath}");

            var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
            var issues = new List<IssueInfo>();

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith('#'))
                    continue;

                var parseResult = await ParseIssueFromUrlAsync(trimmedLine, cancellationToken);
                if (parseResult.IsSuccess)
                    issues.Add(parseResult.Value);
            }

            return Result.Success<IReadOnlyList<IssueInfo>, IssueProcessingError>(
                issues.AsReadOnly()
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse issues from file {FilePath}", filePath);
            return new BatchProcessingFailedError(0, ex.Message);
        }
    }

    public async Task<Result<bool, IssueProcessingError>> ProcessIssueBatchAsync(
        ProcessIssueBatchCommand command,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var issuesResult = await ParseIssuesFromFileAsync(command.InputFile, cancellationToken);
            if (issuesResult.IsFailure)
                return issuesResult.Error;

            var issues = issuesResult.Value;
            logger.LogInformation(
                "Processing {Count} issues from {InputFile}",
                issues.Count,
                command.InputFile
            );

            // Convert to legacy format for existing service
            var issueTasks = issues
                .Select(i => (i.Url, i.Owner, i.Repo, (long?)i.IssueNumber))
                .Distinct()
                .ToList();

            if (command.UseBatchApi)
            {
                await batchProcessingService.ProcessBatchWithOpenAIAsync(
                    issueTasks,
                    command.SaveResults,
                    command.UseCache,
                    command.BatchJobId
                );
            }
            else
            {
                await batchProcessingService.ProcessBatchWithParallelismAsync(
                    issueTasks,
                    command.SaveResults,
                    command.UseCache
                );
            }

            return Result.Success<bool, IssueProcessingError>(true);
        }
        catch (OperationCanceledException)
        {
            return new BatchProcessingFailedError(0, "Operation was cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process batch from {InputFile}", command.InputFile);
            return new BatchProcessingFailedError(0, ex.Message);
        }
    }
}
