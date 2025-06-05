using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ConsoleAppFramework;
using DataCollection.Models.Export.BugAnalysis;
using DataCollection.Options;
using DataCollection.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace DataCollection.Commands;

/// <summary>
/// Commands for discovering bug lists and artifact repositories from papers
/// </summary>
[RegisterCommands("buglist")]
[ConsoleAppFilter<PathsOptions.Filter>]
public class BugListDiscoveryCommands(
    ILogger<BugListDiscoveryCommands> logger,
    BugListDiscoveryService bugListDiscoveryService,
    IOptions<PathsOptions> pathsOptions
)
{
    private const int MaxDisplayResults = 10;
    private const int MaxTitleLength = 50;
    private const int TitleTruncateLength = 47;
    private const string DefaultOutputFileName = "bug-list-discovery.json";

    private readonly PathsOptions _pathsOptions = pathsOptions.Value;

    public async Task<int> Discover(
        string dois,
        string? outputPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var doiList = ValidateAndParseDois(dois);
        if (doiList == null)
            return 1;

        return await ExecuteDiscovery(doiList, outputPath, cancellationToken);
    }

    public async Task<int> FromFile(
        string doiFile,
        string? outputPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var doiList = await LoadDoisFromFile(doiFile, cancellationToken);
        if (doiList == null)
            return 1;

        return await ExecuteDiscovery(doiList, outputPath, cancellationToken);
    }

    private List<string>? ValidateAndParseDois(string dois)
    {
        if (string.IsNullOrWhiteSpace(dois))
        {
            LogAndDisplayError("DOIs parameter is required");
            return null;
        }

        var doiList = dois.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.Trim())
            .ToList();

        if (doiList.Count == 0)
        {
            LogAndDisplayError("No valid DOIs provided");
            return null;
        }

        return doiList;
    }

    private async Task<List<string>?> LoadDoisFromFile(
        string doiFile,
        CancellationToken cancellationToken
    )
    {
        if (!File.Exists(doiFile))
        {
            LogAndDisplayError($"DOI file not found: {doiFile}");
            return null;
        }

        try
        {
            var doiLines = await File.ReadAllLinesAsync(doiFile, cancellationToken);
            var cleanedDois = doiLines
                .Where(line =>
                    !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#")
                )
                .Select(line => line.Trim())
                .ToList();

            if (cleanedDois.Count == 0)
            {
                LogAndDisplayError("No valid DOIs found in file");
                return null;
            }

            AnsiConsole.MarkupLine($"[blue]Loaded {cleanedDois.Count} DOIs from file {doiFile}[/]");
            return cleanedDois;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reading DOI file {DoiFile}: {Message}", doiFile, ex.Message);
            AnsiConsole.MarkupLine($"[red]Error reading file:[/] {ex.Message}");
            return null;
        }
    }

    private async Task<int> ExecuteDiscovery(
        List<string> doiList,
        string? outputPath,
        CancellationToken cancellationToken
    )
    {
        AnsiConsole.MarkupLine(
            $"[blue]Starting bug list discovery for {doiList.Count} papers...[/]"
        );

        try
        {
            var analysis = await bugListDiscoveryService.DiscoverBugListsAsync(
                doiList,
                cancellationToken
            );

            DisplayResults(analysis);
            await ExportAndReportResults(analysis, outputPath);
            LogSuccessfulCompletion(analysis);

            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during bug list discovery: {Message}", ex.Message);
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }

    private async Task ExportAndReportResults(BugListDiscoveryAnalysis analysis, string? outputPath)
    {
        var finalPath = outputPath ?? Path.Combine(_pathsOptions.BaseDir, DefaultOutputFileName);
        await ExportResults(analysis, finalPath);
        AnsiConsole.MarkupLine($"[green]Results exported to:[/] {finalPath}");
    }

    private void LogSuccessfulCompletion(BugListDiscoveryAnalysis analysis)
    {
        logger.LogInformation(
            "Bug list discovery completed. Processed {TotalPapers} papers, found bug lists for {BugListPapers} papers, artifacts for {ArtifactPapers} papers",
            analysis.Summary.TotalPapers,
            analysis.Summary.PapersWithBugLists,
            analysis.Summary.PapersWithArtifacts
        );
    }

    private static void DisplayResults(BugListDiscoveryAnalysis analysis)
    {
        DisplaySummaryTable(analysis.Summary);
        DisplayStatsTables(analysis);
        DisplayIndividualResults(analysis.PaperResults);
    }

    private static void DisplaySummaryTable(BugListDiscoverySummary summary)
    {
        var summaryTable = new Table
        {
            Title = new TableTitle("[bold]Bug List Discovery Summary[/]"),
        };

        summaryTable.AddColumn("Metric");
        summaryTable.AddColumn("Value");

        summaryTable.AddRow("Total Papers", summary.TotalPapers.ToString());
        summaryTable.AddRow("Papers with Bug Lists", summary.PapersWithBugLists.ToString());
        summaryTable.AddRow("Papers with Artifacts", summary.PapersWithArtifacts.ToString());
        summaryTable.AddRow("Failed Discoveries", summary.FailedDiscoveries.ToString());
        summaryTable.AddRow("Success Rate", $"{summary.SuccessRate:F1}%");
        summaryTable.AddRow("Total Bug Lists Found", summary.TotalBugListsFound.ToString());

        AnsiConsole.Write(summaryTable);
    }

    private static void DisplayStatsTables(BugListDiscoveryAnalysis analysis)
    {
        DisplayStatsTable(
            analysis.BugTrackingSystemStats,
            "Bug Tracking System",
            "[bold]Bug Tracking Systems Found[/]"
        );

        DisplayStatsTable(
            analysis.RepositoryTypeStats,
            "Repository Type",
            "[bold]Repository Types Found[/]"
        );
    }

    private static void DisplayStatsTable(
        Dictionary<string, int> stats,
        string columnName,
        string title
    )
    {
        if (!stats.Any())
            return;

        var table = new Table { Title = new TableTitle(title) };

        table.AddColumn(columnName);
        table.AddColumn("Count");

        foreach (var stat in stats.OrderByDescending(s => s.Value))
        {
            table.AddRow(stat.Key, stat.Value.ToString());
        }

        AnsiConsole.Write(table);
    }

    private static void DisplayIndividualResults(List<BugListDiscoveryResult> results)
    {
        var detailsTable = new Table
        {
            Title = new TableTitle($"[bold]Individual Results (Top {MaxDisplayResults})[/]"),
        };

        detailsTable.AddColumn("Paper Title");
        detailsTable.AddColumn("Bug Lists");
        detailsTable.AddColumn("Issue Numbers");
        detailsTable.AddColumn("Artifacts");
        detailsTable.AddColumn("Status");

        foreach (var result in results.Take(MaxDisplayResults))
        {
            var status = GetResultStatus(result);
            var truncatedTitle = TruncateTitle(result.Title);
            var issueNumbersDisplay = GetIssueNumbersDisplay(result.BugLists);

            detailsTable.AddRow(
                truncatedTitle,
                result.BugLists.Count.ToString(),
                issueNumbersDisplay,
                result.ArtifactRepositories.Count.ToString(),
                status
            );
        }

        AnsiConsole.Write(detailsTable);

        if (results.Count > MaxDisplayResults)
        {
            AnsiConsole.MarkupLine(
                $"[dim]... and {results.Count - MaxDisplayResults} more results (see exported JSON for full details)[/]"
            );
        }

        // Display detailed bug lists for papers with extracted issue numbers
        DisplayBugListDetails(results.Take(MaxDisplayResults).ToList());
    }

    private static string GetIssueNumbersDisplay(List<BugListSource> bugLists)
    {
        var totalIssues = bugLists.Sum(bl => bl.IssueNumbers.Count);
        if (totalIssues == 0)
            return "0";

        var sampleIssues = bugLists.SelectMany(bl => bl.IssueNumbers).Take(3).ToList();

        var display = string.Join(", ", sampleIssues.Select(ConsoleRenderingService.SafeMarkup));
        if (totalIssues > 3)
            display += $" +{totalIssues - 3} more";

        return display;
    }

    private static void DisplayBugListDetails(List<BugListDiscoveryResult> results)
    {
        var resultsWithBugLists = results
            .Where(r => r.BugLists.Any(bl => bl.IssueNumbers.Count > 0))
            .ToList();

        if (!resultsWithBugLists.Any())
            return;

        AnsiConsole.Write(new Rule("[bold]Extracted Bug Lists[/]").RuleStyle("green"));

        foreach (var result in resultsWithBugLists)
        {
            var bugListsWithIssues = result.BugLists.Where(bl => bl.IssueNumbers.Count > 0);

            foreach (var bugList in bugListsWithIssues)
            {
                var safeTableContext = ConsoleRenderingService.SafeMarkup(
                    bugList.TableContext ?? "Unknown"
                );
                var safeUrl = ConsoleRenderingService.SafeMarkup(bugList.Url);
                var safeIssueNumbers = ConsoleRenderingService.SafeMarkup(
                    string.Join(", ", bugList.IssueNumbers)
                );

                var panel = new Panel(
                    $"""
                    [bold]Table Context:[/] {safeTableContext}
                    [bold]Repository:[/] {safeUrl}
                    [bold]Issue Numbers:[/] {safeIssueNumbers}
                    [bold]Confidence:[/] {bugList.Confidence:F2}
                    [bold]Discovery Method:[/] {ConsoleRenderingService.SafeMarkup(
                        bugList.DiscoveryMethod
                    )}
                    """
                )
                {
                    Header = new PanelHeader(
                        $"[bold]{ConsoleRenderingService.SafeMarkup(TruncateTitle(result.Title))}[/]"
                    ),
                    Border = BoxBorder.Rounded,
                };

                AnsiConsole.Write(panel);
            }
        }

        // Also display bug lists without issue numbers (URL-only findings)
        var resultsWithUrlBugLists = results
            .Where(r => r.BugLists.Any(bl => bl.IssueNumbers.Count == 0))
            .ToList();

        if (resultsWithUrlBugLists.Any())
        {
            AnsiConsole.Write(new Rule("[bold]Bug Tracking URLs Found[/]").RuleStyle("yellow"));

            foreach (var result in resultsWithUrlBugLists)
            {
                var urlOnlyBugLists = result.BugLists.Where(bl => bl.IssueNumbers.Count == 0);

                foreach (var bugList in urlOnlyBugLists)
                {
                    var safeUrl = ConsoleRenderingService.SafeMarkup(bugList.Url);
                    var safeType = ConsoleRenderingService.SafeMarkup(bugList.Type);
                    var safeMethod = ConsoleRenderingService.SafeMarkup(bugList.DiscoveryMethod);

                    AnsiConsole.MarkupLine(
                        $"  [blue]•[/] [bold]{safeType}:[/] {safeUrl} [dim]({safeMethod}, confidence: {bugList.Confidence:F2})[/]"
                    );
                }
            }
        }
    }

    private static string GetResultStatus(BugListDiscoveryResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            return "[red]Error[/]";

        return result.DiscoverySuccessful ? "[green]Success[/]" : "[red]Failed[/]";
    }

    private static string TruncateTitle(string title)
    {
        return title.Length > MaxTitleLength
            ? title.Substring(0, TitleTruncateLength) + "..."
            : title;
    }

    private async Task ExportResults(BugListDiscoveryAnalysis analysis, string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(
            analysis,
            ExportModelJsonContext.Default.BugListDiscoveryAnalysis
        );

        await File.WriteAllTextAsync(outputPath, json);
        logger.LogInformation("Results exported to {OutputPath}", outputPath);
    }

    private void LogAndDisplayError(string message)
    {
        logger.LogError(message);
        AnsiConsole.MarkupLine($"[red]Error:[/] {message}");
    }
}
