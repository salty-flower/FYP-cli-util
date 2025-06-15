using DataCollection.Core.Models.Errors;
using DataCollection.Infrastructure.Models.BugList;
using DataCollection.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace DataCollection.Presentation.Cli.Services;

public class BugListDiscoveryResultRenderer : IResultRenderer<BugListDiscoveryAnalysis>
{
    private readonly BugListDiscoveryOptions options;

    public BugListDiscoveryResultRenderer(IOptions<BugListDiscoveryOptions> options)
    {
        this.options = options.Value;
    }

    public Task RenderAsync(
        BugListDiscoveryAnalysis analysis,
        CancellationToken cancellationToken = default
    )
    {
        DisplaySummaryTable(analysis.Summary);
        DisplayStatsTables(analysis);
        DisplayIndividualResults(analysis.PaperResults);
        return Task.CompletedTask;
    }

    private static void DisplaySummaryTable(BugListDiscoverySummary summary)
    {
        var table = new Table();
        table.AddColumn("Metric");
        table.AddColumn("Value");
        table.Title = new TableTitle("[bold]Discovery Summary[/]");

        table.AddRow("Papers Processed", summary.TotalPapersProcessed.ToString());
        table.AddRow("Papers with Bug Lists", summary.PapersWithBugLists.ToString());
        table.AddRow("Papers with Artifacts", summary.PapersWithArtifacts.ToString());
        table.AddRow("Total Bug Lists Found", summary.TotalBugListsFound.ToString());
        table.AddRow("Total Artifacts Found", summary.TotalArtifactsFound.ToString());
        table.AddRow("Success Rate", $"{summary.SuccessRate:P1}");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static void DisplayStatsTables(BugListDiscoveryAnalysis analysis)
    {
        if (analysis.BugTrackingSystemStats.Count > 0)
        {
            DisplayStatsTable(
                analysis.BugTrackingSystemStats,
                "Bug Tracking System",
                "Bug Tracking Systems"
            );
        }

        if (analysis.RepositoryTypeStats.Count > 0)
        {
            DisplayStatsTable(analysis.RepositoryTypeStats, "Repository Type", "Repository Types");
        }
    }

    private static void DisplayStatsTable(
        Dictionary<string, int> stats,
        string columnName,
        string title
    )
    {
        var table = new Table();
        table.AddColumn(columnName);
        table.AddColumn("Count");
        table.Title = new TableTitle($"[bold]{title}[/]");

        foreach (var (key, value) in stats.OrderByDescending(kvp => kvp.Value))
        {
            table.AddRow(key, value.ToString());
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private void DisplayIndividualResults(List<BugListDiscoveryResult> results)
    {
        if (results.Count == 0)
            return;

        var table = new Table();
        table.AddColumn("DOI");
        table.AddColumn("Title");
        table.AddColumn("Bug Lists");
        table.AddColumn("Artifacts");
        table.AddColumn("Status");
        table.Title = new TableTitle(
            $"[bold]Individual Results (Top {options.MaxDisplayResults})[/]"
        );

        foreach (var result in results.Take(options.MaxDisplayResults))
        {
            table.AddRow(
                result.Doi,
                TruncateTitle(result.Title),
                result.BugLists.Count.ToString(),
                result.ArtifactRepositories.Count.ToString(),
                GetResultStatus(result)
            );
        }

        AnsiConsole.Write(table);

        if (results.Count > options.MaxDisplayResults)
        {
            AnsiConsole.MarkupLine(
                $"[dim]... and {results.Count - options.MaxDisplayResults} more results (see exported JSON for full details)[/]"
            );
        }

        AnsiConsole.WriteLine();
        DisplayBugListDetails(results.Take(options.MaxDisplayResults).ToList());
    }

    private static void DisplayBugListDetails(List<BugListDiscoveryResult> results)
    {
        foreach (var result in results)
        {
            if (result.BugLists.Count == 0 && result.ArtifactRepositories.Count == 0)
                continue;

            AnsiConsole.MarkupLine($"[bold]{result.Doi}[/]: [italic]{result.Title}[/]");

            if (result.BugLists.Count > 0)
            {
                AnsiConsole.MarkupLine($"  [green]Bug Lists ({result.BugLists.Count}):[/]");
                foreach (var bugList in result.BugLists)
                {
                    var confidence = $"[dim](confidence: {bugList.Confidence:F2})[/]";
                    var method = $"[dim]{bugList.DiscoveryMethod}[/]";
                    AnsiConsole.MarkupLine($"    • {bugList.Url} {confidence} via {method}");
                }
            }

            if (result.ArtifactRepositories.Count > 0)
            {
                AnsiConsole.MarkupLine(
                    $"  [blue]Artifact Repositories ({result.ArtifactRepositories.Count}):[/]"
                );
                foreach (var repo in result.ArtifactRepositories)
                {
                    var confidence = $"[dim](confidence: {repo.Confidence:F2})[/]";
                    var method = $"[dim]{repo.DiscoveryMethod}[/]";
                    AnsiConsole.MarkupLine($"    • {repo.Url} {confidence} via {method}");
                }
            }

            AnsiConsole.WriteLine();
        }
    }

    private static string GetResultStatus(BugListDiscoveryResult result)
    {
        return (result.BugLists.Count, result.ArtifactRepositories.Count) switch
        {
            (0, 0) => "[red]No Results[/]",
            (> 0, 0) => "[yellow]Bug Lists Only[/]",
            (0, > 0) => "[blue]Artifacts Only[/]",
            _ => "[green]Complete[/]",
        };
    }

    private string TruncateTitle(string title)
    {
        return title.Length > options.MaxTitleLength
            ? title[..options.TitleTruncateLength] + "..."
            : title;
    }
}

public class BugListDiscoveryErrorRenderer : IErrorRenderer
{
    public Task RenderAsync(object error, CancellationToken cancellationToken = default)
    {
        switch (error)
        {
            case BugListDiscoveryError bugListError:
                AnsiConsole.MarkupLine(
                    $"[red]Error [{bugListError.Code}]:[/] {bugListError.Message}"
                );
                break;
            default:
                AnsiConsole.MarkupLine($"[red]Unexpected error:[/] {error}");
                break;
        }

        return Task.CompletedTask;
    }
}
