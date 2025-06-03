using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ConsoleAppFramework;
using DataCollection.Filters;
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
    private readonly PathsOptions _pathsOptions = pathsOptions.Value;

    /// <summary>
    /// Discover bug lists and artifact repositories for papers by DOI
    /// </summary>
    /// <param name="dois">Comma-separated list of DOIs to process</param>
    /// <param name="outputPath">Output path for the analysis results (JSON)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of papers processed</returns>
    public async Task<int> Discover(
        string dois,
        string? outputPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(dois))
        {
            logger.LogError("DOIs parameter is required");
            AnsiConsole.MarkupLine("[red]Error:[/] DOIs parameter is required");
            return 1;
        }

        var doiList = dois.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.Trim())
            .ToList();

        if (doiList.Count == 0)
        {
            logger.LogError("No valid DOIs provided");
            AnsiConsole.MarkupLine("[red]Error:[/] No valid DOIs provided");
            return 1;
        }

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

            // Export results if output path is provided
            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                await ExportResults(analysis, outputPath);
                AnsiConsole.MarkupLine($"[green]Results exported to:[/] {outputPath}");
            }
            else
            {
                // Default export path
                var defaultPath = Path.Combine(_pathsOptions.BaseDir, "bug-list-discovery.json");
                await ExportResults(analysis, defaultPath);
                AnsiConsole.MarkupLine($"[green]Results exported to:[/] {defaultPath}");
            }

            logger.LogInformation(
                "Bug list discovery completed. Processed {TotalPapers} papers, found bug lists for {BugListPapers} papers, artifacts for {ArtifactPapers} papers",
                analysis.Summary.TotalPapers,
                analysis.Summary.PapersWithBugLists,
                analysis.Summary.PapersWithArtifacts
            );

            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during bug list discovery: {Message}", ex.Message);
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Discover bug lists from a file containing DOIs (one per line)
    /// </summary>
    /// <param name="doiFile">Path to file containing DOIs (one per line)</param>
    /// <param name="outputPath">Output path for the analysis results (JSON)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of papers processed</returns>
    public async Task<int> FromFile(
        string doiFile,
        string? outputPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!File.Exists(doiFile))
        {
            logger.LogError("DOI file not found: {DoiFile}", doiFile);
            AnsiConsole.MarkupLine($"[red]Error:[/] DOI file not found: {doiFile}");
            return 1;
        }

        try
        {
            var doiList = await File.ReadAllLinesAsync(doiFile, cancellationToken);
            var cleanedDois = doiList
                .Where(line =>
                    !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#")
                )
                .Select(line => line.Trim())
                .ToList();

            if (cleanedDois.Count == 0)
            {
                logger.LogError("No valid DOIs found in file: {DoiFile}", doiFile);
                AnsiConsole.MarkupLine($"[red]Error:[/] No valid DOIs found in file");
                return 1;
            }

            AnsiConsole.MarkupLine($"[blue]Loaded {cleanedDois.Count} DOIs from file {doiFile}[/]");

            return await Discover(string.Join(",", cleanedDois), outputPath, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reading DOI file {DoiFile}: {Message}", doiFile, ex.Message);
            AnsiConsole.MarkupLine($"[red]Error reading file:[/] {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Display results in a formatted table
    /// </summary>
    private void DisplayResults(BugListDiscoveryAnalysis analysis)
    {
        // Summary table
        var summaryTable = new Table();
        summaryTable.AddColumn("Metric");
        summaryTable.AddColumn("Value");
        summaryTable.Title = new TableTitle("[bold]Bug List Discovery Summary[/]");

        summaryTable.AddRow("Total Papers", analysis.Summary.TotalPapers.ToString());
        summaryTable.AddRow(
            "Papers with Bug Lists",
            analysis.Summary.PapersWithBugLists.ToString()
        );
        summaryTable.AddRow(
            "Papers with Artifacts",
            analysis.Summary.PapersWithArtifacts.ToString()
        );
        summaryTable.AddRow("Failed Discoveries", analysis.Summary.FailedDiscoveries.ToString());
        summaryTable.AddRow("Success Rate", $"{analysis.Summary.SuccessRate:F1}%");
        summaryTable.AddRow(
            "Total Search Attempts",
            analysis.Summary.TotalSearchAttempts.ToString()
        );

        AnsiConsole.Write(summaryTable);

        // Bug tracking systems statistics
        if (analysis.BugTrackingSystemStats.Any())
        {
            var bugTable = new Table();
            bugTable.AddColumn("Bug Tracking System");
            bugTable.AddColumn("Count");
            bugTable.Title = new TableTitle("[bold]Bug Tracking Systems Found[/]");

            foreach (var stat in analysis.BugTrackingSystemStats.OrderByDescending(s => s.Value))
            {
                bugTable.AddRow(stat.Key, stat.Value.ToString());
            }

            AnsiConsole.Write(bugTable);
        }

        // Repository types statistics
        if (analysis.RepositoryTypeStats.Any())
        {
            var repoTable = new Table();
            repoTable.AddColumn("Repository Type");
            repoTable.AddColumn("Count");
            repoTable.Title = new TableTitle("[bold]Repository Types Found[/]");

            foreach (var stat in analysis.RepositoryTypeStats.OrderByDescending(s => s.Value))
            {
                repoTable.AddRow(stat.Key, stat.Value.ToString());
            }

            AnsiConsole.Write(repoTable);
        }

        // Individual results (top 10)
        var detailsTable = new Table();
        detailsTable.AddColumn("Paper Title");
        detailsTable.AddColumn("Bug Lists");
        detailsTable.AddColumn("Artifacts");
        detailsTable.AddColumn("Status");
        detailsTable.Title = new TableTitle("[bold]Individual Results (Top 10)[/]");

        foreach (var result in analysis.PaperResults.Take(10))
        {
            var status = result.DiscoverySuccessful ? "[green]Success[/]" : "[red]Failed[/]";
            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                status = $"[red]Error[/]";
            }

            detailsTable.AddRow(
                result.Title.Length > 50 ? result.Title.Substring(0, 47) + "..." : result.Title,
                result.BugLists.Count.ToString(),
                result.ArtifactRepositories.Count.ToString(),
                status
            );
        }

        AnsiConsole.Write(detailsTable);

        if (analysis.PaperResults.Count > 10)
        {
            AnsiConsole.MarkupLine(
                $"[dim]... and {analysis.PaperResults.Count - 10} more results (see exported JSON for full details)[/]"
            );
        }
    }

    /// <summary>
    /// Export results to JSON file
    /// </summary>
    private async Task ExportResults(BugListDiscoveryAnalysis analysis, string outputPath)
    {
        // Ensure directory exists
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
}
