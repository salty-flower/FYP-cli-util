using ConsoleAppFramework;
using CSharpFunctionalExtensions;
using DataCollection.Application.Models.Commands;
using DataCollection.Application.Services;
using DataCollection.Core.Models.Errors;
using DataCollection.Presentation.Cli.Filters;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace DataCollection.Presentation.Cli.Commands;

[RegisterCommands("issue-v2")]
[ConsoleAppFilter<PathsOptionsFilter>]
[ConsoleAppFilter<CredentialOptionsFilter>]
public class SimplifiedIssueCommands(
    IIssueProcessingService issueProcessingService,
    ILogger<SimplifiedIssueCommands> logger
)
{
    public async Task<int> DecideStatus(
        string? url = null,
        string? owner = null,
        string? repoName = null,
        long? issueNumber = null,
        bool saveResults = false,
        bool useCache = true,
        CancellationToken cancellationToken = default
    )
    {
        var command = new DecideIssueStatusCommand(
            url,
            owner,
            repoName,
            issueNumber,
            saveResults,
            useCache
        );
        var result = await issueProcessingService.ProcessSingleIssueAsync(
            command,
            cancellationToken
        );

        return await result.Match(
            success =>
            {
                var issueDisplay = url ?? $"{owner}/{repoName}#{issueNumber}";
                AnsiConsole.MarkupLine($"[green]✓[/] Successfully processed issue: {issueDisplay}");
                logger.LogInformation("Successfully processed issue {Issue}", issueDisplay);
                return Task.FromResult(0);
            },
            error =>
            {
                AnsiConsole.MarkupLine($"[red]✗[/] {error.Message}");
                logger.LogError("Failed to process issue: {Error}", error.Message);
                return Task.FromResult(1);
            }
        );
    }

    public async Task<int> ProcessBatch(
        string inputFile,
        bool saveResults = true,
        bool useCache = true,
        bool useBatchApi = true,
        string? batchJobId = null,
        CancellationToken cancellationToken = default
    )
    {
        var command = new ProcessIssueBatchCommand(
            inputFile,
            saveResults,
            useCache,
            useBatchApi,
            batchJobId
        );

        // First, validate the file and count issues
        var issuesResult = await issueProcessingService.ParseIssuesFromFileAsync(
            inputFile,
            cancellationToken
        );

        return await issuesResult.Match(
            async issues =>
            {
                AnsiConsole.MarkupLine(
                    $"[blue]Found {issues.Count} valid issues in {inputFile}[/]"
                );

                if (issues.Count == 0)
                {
                    AnsiConsole.MarkupLine("[yellow]No valid issues found to process[/]");
                    return 0;
                }

                // Show processing method
                var method = useBatchApi ? "OpenAI Batch API" : "Parallel Processing";
                AnsiConsole.MarkupLine($"[blue]Processing using: {method}[/]");

                var processResult = await issueProcessingService.ProcessIssueBatchAsync(
                    command,
                    cancellationToken
                );

                return await processResult.Match(
                    success =>
                    {
                        AnsiConsole.MarkupLine(
                            $"[green]✓[/] Successfully processed {issues.Count} issues"
                        );
                        logger.LogInformation(
                            "Successfully processed batch of {Count} issues from {File}",
                            issues.Count,
                            inputFile
                        );
                        return Task.FromResult(0);
                    },
                    error =>
                    {
                        AnsiConsole.MarkupLine(
                            $"[red]✗[/] Batch processing failed: {error.Message}"
                        );
                        logger.LogError("Batch processing failed: {Error}", error.Message);
                        return Task.FromResult(1);
                    }
                );
            },
            async error =>
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Failed to parse issues: {error.Message}");
                logger.LogError(
                    "Failed to parse issues from file {File}: {Error}",
                    inputFile,
                    error.Message
                );
                return 1;
            }
        );
    }

    public async Task<int> ValidateFile(
        string inputFile,
        CancellationToken cancellationToken = default
    )
    {
        AnsiConsole.MarkupLine($"[blue]Validating issue URLs in {inputFile}...[/]");

        var result = await issueProcessingService.ParseIssuesFromFileAsync(
            inputFile,
            cancellationToken
        );

        return await result.Match(
            issues =>
            {
                var table = new Table();
                table.AddColumn("Owner");
                table.AddColumn("Repository");
                table.AddColumn("Issue #");
                table.AddColumn("URL");

                foreach (var issue in issues.Take(10)) // Show first 10
                {
                    table.AddRow(issue.Owner, issue.Repo, issue.IssueNumber.ToString(), issue.Url);
                }

                AnsiConsole.Write(table);

                if (issues.Count > 10)
                {
                    AnsiConsole.MarkupLine($"[dim]... and {issues.Count - 10} more issues[/]");
                }

                AnsiConsole.MarkupLine($"[green]✓[/] Found {issues.Count} valid GitHub issue URLs");
                return Task.FromResult(0);
            },
            error =>
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Validation failed: {error.Message}");
                return Task.FromResult(1);
            }
        );
    }
}
