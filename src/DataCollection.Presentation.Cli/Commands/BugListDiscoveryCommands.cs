using System.Text.Json;
using ConsoleAppFramework;
using DataCollection.Application.Features.BugDiscovery;
using DataCollection.Application.Features.SemanticAgents;
using DataCollection.Application.Features.SemanticAgents.Models;
using DataCollection.Application.Models.Export;
using DataCollection.Infrastructure.Clients;
using DataCollection.Infrastructure.Models.BugList;
using DataCollection.Infrastructure.Options;
using DataCollection.Presentation.Cli.Commands.Helpers;
using DataCollection.Presentation.Cli.Filters;
using DataCollection.Presentation.Cli.Rendering;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;
using ExportModelJsonContext = DataCollection.Application.Models.Export.ExportModelJsonContext;

namespace DataCollection.Presentation.Cli.Commands;

/// <summary>
/// Commands for discovering bug lists and artifact repositories from papers
/// </summary>
[RegisterCommands("buglist")]
[ConsoleAppFilter<PathsOptionsFilter>]
public class BugListDiscoveryCommands(
    ILogger<BugListDiscoveryCommands> logger,
    DatabaseBugListDiscoveryStorageService databaseStorageService,
    IDiscoveryAgentService? discoveryAgentService,
    IOptions<PathsOptions> pathsOptions,
    IOptions<RootOptions> rootOptions
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
                    !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#')
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

    public async Task<int> AgentDiscover(
        string content,
        DiscoveryTaskType taskType = DiscoveryTaskType.BugListDiscovery,
        string? keywords = null,
        string? outputPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (discoveryAgentService == null)
        {
            LogAndDisplayError(
                "Discovery agent service is not available. Ensure SemanticKernel is properly configured."
            );
            return 1;
        }

        try
        {
            var keywordList = string.IsNullOrWhiteSpace(keywords)
                ? null
                : keywords
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(k => k.Trim())
                    .ToList();

            AnsiConsole.MarkupLine($"[blue]Starting agent-based {taskType} discovery...[/]");

            var results = taskType switch
            {
                DiscoveryTaskType.BugListDiscovery =>
                    await discoveryAgentService.DiscoverBugListsAsync(content, keywordList),
                DiscoveryTaskType.ArtifactRepositoryDiscovery =>
                    await discoveryAgentService.DiscoverArtifactRepositoriesAsync(
                        content,
                        keywordList
                    ),
                DiscoveryTaskType.VulnerabilityDiscovery =>
                    await discoveryAgentService.DiscoverVulnerabilitiesAsync(content, keywordList),
                _ => throw new ArgumentException($"Unsupported task type: {taskType}"),
            };

            DisplayAgentResults(results, taskType);
            await ExportAgentResults(results, outputPath, taskType);
            LogAgentDiscoveryCompletion(results, taskType);

            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during agent-based discovery: {Message}", ex.Message);
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }

    public async Task<int> AgentFromPdf(
        string pdfPath,
        DiscoveryTaskType taskType = DiscoveryTaskType.BugListDiscovery,
        string? keywords = null,
        string? outputPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (discoveryAgentService == null)
        {
            LogAndDisplayError(
                "Discovery agent service is not available. Ensure SemanticKernel is properly configured."
            );
            return 1;
        }

        if (!File.Exists(pdfPath))
        {
            LogAndDisplayError($"PDF file not found: {pdfPath}");
            return 1;
        }

        try
        {
            var keywordList = string.IsNullOrWhiteSpace(keywords)
                ? null
                : keywords
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(k => k.Trim())
                    .ToList();

            AnsiConsole.MarkupLine(
                $"[blue]Starting agent-based {taskType} discovery from PDF: {Path.GetFileName(pdfPath)}[/]"
            );

            var results = await discoveryAgentService.DiscoverFromPdfAsync(
                pdfPath,
                taskType,
                keywordList
            );

            DisplayAgentResults(results, taskType);
            await ExportAgentResults(results, outputPath, taskType);
            LogAgentDiscoveryCompletion(results, taskType);

            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during PDF agent-based discovery: {Message}", ex.Message);
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }

    public async Task<int> ComprehensiveBugListDiscovery(
        string doi,
        string? outputPath = null,
        bool skipArtifactAnalysis = false,
        bool searchExternalPlatforms = true,
        CancellationToken cancellationToken = default
    )
    {
        if (discoveryAgentService == null)
        {
            LogAndDisplayError(
                "Discovery agent service is not available. This command requires SemanticKernel agents."
            );
            return 1;
        }

        AnsiConsole.MarkupLine(
            $"[blue]Starting comprehensive bug list discovery for DOI: {doi}[/]"
        );
        AnsiConsole.MarkupLine(
            "[dim]This process searches for bug lists in paper content AND artifact repositories[/]"
        );

        try
        {
            var paperContent = $"Analyzing paper content for DOI: {doi}"; // Placeholder for content extraction
            var paperMetadata = PaperMetadataExtractor.ExtractMetadata(doi, paperContent);

            var directBugListResults = await PaperAnalysisHelpers.AnalyzePaperContent(
                discoveryAgentService,
                doi,
                paperContent,
                paperMetadata
            );
            var allResults = new List<DiscoveryResult>(directBugListResults);
            var discoveredArtifacts = new List<DiscoveryResult>();

            if (!skipArtifactAnalysis)
            {
                var paperArtifacts = await PaperAnalysisHelpers.DiscoverArtifacts(
                    discoveryAgentService,
                    paperContent,
                    paperMetadata
                );
                discoveredArtifacts.AddRange(paperArtifacts);

                if (searchExternalPlatforms)
                {
                    var externalArtifacts = await PaperAnalysisHelpers.SearchExternalPlatforms(
                        discoveryAgentService,
                        $"Search terms for {doi}",
                        paperMetadata
                    );
                    discoveredArtifacts.AddRange(externalArtifacts);
                }

                if (discoveredArtifacts.Any())
                {
                    var artifactBugResults = await PaperAnalysisHelpers.AnalyzeArtifacts(
                        discoveryAgentService,
                        discoveredArtifacts,
                        paperMetadata
                    );
                    allResults.AddRange(artifactBugResults);
                }
            }

            DisplayComprehensiveResults(allResults, discoveredArtifacts, doi);
            await ExportComprehensiveResults(allResults, discoveredArtifacts, outputPath, doi);
            LogComprehensiveDiscoveryCompletion(allResults, doi);

            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error during comprehensive bug list discovery: {Message}",
                ex.Message
            );
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }

    private async Task<int> ExecuteDiscovery(
        List<string> doiList,
        string? outputPath,
        CancellationToken cancellationToken
    )
    {
        if (discoveryAgentService == null)
        {
            LogAndDisplayError(
                "Agent-based discovery requested but agent service is not available."
            );
            return 1;
        }

        AnsiConsole.MarkupLine(
            $"[blue]Starting agent-based bug list discovery for {doiList.Count} papers...[/]"
        );

        // For agent-based discovery, we'd need to process each DOI individually
        // This is a simplified implementation - in practice, you'd extract content from each paper
        var agentResults = new List<DiscoveryResult>();

        foreach (var doi in doiList)
        {
            // Placeholder: In real implementation, extract paper content here
            var paperContent = $"Processing paper with DOI: {doi}";
            var paperMetadata = PaperMetadataExtractor.ExtractMetadata(doi, paperContent);
            var results = await discoveryAgentService!.DiscoverBugListsAsync(
                paperContent,
                null,
                paperMetadata
            );
            agentResults.AddRange(results);
        }

        DisplayAgentResults(agentResults, DiscoveryTaskType.BugListDiscovery);
        await ExportAgentResults(agentResults, outputPath, DiscoveryTaskType.BugListDiscovery);
        LogAgentDiscoveryCompletion(agentResults, DiscoveryTaskType.BugListDiscovery);

        return 0;
    }

    private async Task ExportAndReportResults(BugListDiscoveryAnalysis analysis, string? outputPath)
    {
        var finalPath = outputPath ?? Path.Combine(_pathsOptions.BaseDir, DefaultOutputFileName);
        await ExportResults(analysis, finalPath);
        await SaveToDatabase(analysis);
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

    private async Task SaveToDatabase(BugListDiscoveryAnalysis analysis)
    {
        try
        {
            rootOptions.Value.TryParseJobName(out var jobName);
            await databaseStorageService.SaveBugListDiscoveryAsync(jobName!.Value, analysis);
            logger.LogInformation("Bug list discovery analysis saved to database");
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to save bug list discovery to database: {Error}",
                ex.Message
            );
        }
    }

    private static void DisplayAgentResults(
        List<DiscoveryResult> results,
        DiscoveryTaskType taskType
    )
    {
        var table = new Table
        {
            Title = new TableTitle($"[bold]Agent Discovery Results: {taskType}[/]"),
        };

        table.AddColumn("Title");
        table.AddColumn("Type");
        table.AddColumn("Confidence");
        table.AddColumn("URL");
        table.AddColumn("Context");

        foreach (var result in results.Take(MaxDisplayResults))
        {
            var safeTitle = ConsoleRenderingService.SafeMarkup(result.Title);
            var safeType = ConsoleRenderingService.SafeMarkup(result.Type);
            var safeUrl = ConsoleRenderingService.SafeMarkup(result.Url);
            var contextText = result.Context.SurroundingText.Any()
                ? string.Join(" ", result.Context.SurroundingText)
                : result.Context.SourceLocation ?? "No context";
            var safeContext = ConsoleRenderingService.SafeMarkup(
                contextText.Length > 50 ? contextText.Substring(0, 47) + "..." : contextText
            );

            table.AddRow(safeTitle, safeType, $"{result.Confidence:F2}", safeUrl, safeContext);
        }

        AnsiConsole.Write(table);

        if (results.Count > MaxDisplayResults)
        {
            AnsiConsole.MarkupLine(
                $"[dim]... and {results.Count - MaxDisplayResults} more results[/]"
            );
        }

        // Display high-confidence results in detail
        var highConfidenceResults = results.Where(r => r.Confidence > 0.8).ToList();
        if (highConfidenceResults.Any())
        {
            AnsiConsole.Write(new Rule("[bold]High Confidence Discoveries[/]").RuleStyle("green"));

            foreach (var result in highConfidenceResults)
            {
                var contextText = result.Context.SurroundingText.Any()
                    ? string.Join(" ", result.Context.SurroundingText)
                    : result.Context.SourceLocation ?? "No context";

                var panel = new Panel(
                    $"""
                    [bold]Title:[/] {ConsoleRenderingService.SafeMarkup(result.Title)}
                    [bold]Type:[/] {ConsoleRenderingService.SafeMarkup(result.Type)}
                    [bold]Confidence:[/] {result.Confidence:F2}
                    [bold]Context:[/] {ConsoleRenderingService.SafeMarkup(contextText)}
                    [bold]URL:[/] {ConsoleRenderingService.SafeMarkup(result.Url)}
                    [bold]Description:[/] {ConsoleRenderingService.SafeMarkup(
                        result.Description ?? "No description"
                    )}
                    """
                )
                {
                    Header = new PanelHeader($"[bold]Discovery: {result.Type}[/]"),
                    Border = BoxBorder.Rounded,
                };

                AnsiConsole.Write(panel);
            }
        }
    }

    private async Task ExportAgentResults(
        List<DiscoveryResult> results,
        string? outputPath,
        DiscoveryTaskType taskType
    )
    {
        var finalPath =
            outputPath
            ?? Path.Combine(
                _pathsOptions.BaseDir,
                $"agent-discovery-{taskType.ToString().ToLowerInvariant()}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json"
            );

        var directory = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var exportData = new AgentDiscoveryExport
        {
            TaskType = taskType.ToString(),
            Timestamp = DateTime.UtcNow,
            Count = results.Count,
            Results = results,
        };

        var json = JsonSerializer.Serialize(
            exportData,
            ExportModelJsonContext.Default.AgentDiscoveryExport
        );
        await File.WriteAllTextAsync(finalPath, json);
        AnsiConsole.MarkupLine(
            $"[green]Successfully exported {results.Count} results to {finalPath}[/]"
        );
    }

    private void LogAgentDiscoveryCompletion(
        List<DiscoveryResult> results,
        DiscoveryTaskType taskType
    )
    {
        var highConfidenceCount = results.Count(r => r.Confidence > 0.8);
        var mediumConfidenceCount = results.Count(r => r.Confidence is > 0.6 and <= 0.8);
        var lowConfidenceCount = results.Count(r => r.Confidence <= 0.6);

        logger.LogInformation(
            "Agent-based {TaskType} discovery completed. Found {TotalResults} results: {HighConfidence} high confidence, {MediumConfidence} medium confidence, {LowConfidence} low confidence",
            taskType,
            results.Count,
            highConfidenceCount,
            mediumConfidenceCount,
            lowConfidenceCount
        );
    }

    private static void DisplayComprehensiveResults(
        List<DiscoveryResult> allResults,
        List<DiscoveryResult> discoveredArtifacts,
        string doi
    )
    {
        AnsiConsole.Write(
            new Rule($"[bold]Comprehensive Bug List Discovery Results for: {doi}[/]").RuleStyle(
                "blue"
            )
        );

        // Group results by source type
        var paperResults = allResults
            .Where(r =>
                !r.Metadata.ContainsKey("source_type")
                || r.Metadata["source_type"] != "artifact_repository"
            )
            .ToList();
        var artifactResults = allResults
            .Where(r =>
                r.Metadata.ContainsKey("source_type")
                && r.Metadata["source_type"] == "artifact_repository"
            )
            .ToList();

        // Display paper-based discoveries
        if (paperResults.Any())
        {
            AnsiConsole.Write(new Rule("[green]Bug Lists Found in Paper Content[/]"));
            var paperTable = new Table();
            paperTable.AddColumn("Title");
            paperTable.AddColumn("Type");
            paperTable.AddColumn("Confidence");
            paperTable.AddColumn("Context");

            foreach (var result in paperResults.Take(MaxDisplayResults))
            {
                var contextText = result.Context.SurroundingText.Any()
                    ? string.Join(" ", result.Context.SurroundingText)
                    : result.Context.SourceLocation ?? "No context";

                paperTable.AddRow(
                    ConsoleRenderingService.SafeMarkup(result.Title),
                    ConsoleRenderingService.SafeMarkup(result.Type),
                    $"{result.Confidence:F2}",
                    ConsoleRenderingService.SafeMarkup(
                        contextText.Length > 50 ? contextText.Substring(0, 47) + "..." : contextText
                    )
                );
            }
            AnsiConsole.Write(paperTable);
        }

        // Display artifact repository discoveries
        if (artifactResults.Any())
        {
            AnsiConsole.Write(new Rule("[yellow]Bug Lists Found in Artifact Repositories[/]"));
            var artifactTable = new Table();
            artifactTable.AddColumn("Title");
            artifactTable.AddColumn("Type");
            artifactTable.AddColumn("Confidence");
            artifactTable.AddColumn("Repository");
            artifactTable.AddColumn("Context");

            foreach (var result in artifactResults.Take(MaxDisplayResults))
            {
                var repoUrl = result.Metadata.TryGetValue("repository_url", out var url)
                    ? url
                    : "Unknown";
                var contextText = result.Context.SurroundingText.Any()
                    ? string.Join(" ", result.Context.SurroundingText)
                    : result.Context.SourceLocation ?? "No context";

                artifactTable.AddRow(
                    ConsoleRenderingService.SafeMarkup(result.Title),
                    ConsoleRenderingService.SafeMarkup(result.Type),
                    $"{result.Confidence:F2}",
                    ConsoleRenderingService.SafeMarkup(
                        repoUrl.Length > 30 ? repoUrl.Substring(0, 27) + "..." : repoUrl
                    ),
                    ConsoleRenderingService.SafeMarkup(
                        contextText.Length > 40 ? contextText.Substring(0, 37) + "..." : contextText
                    )
                );
            }
            AnsiConsole.Write(artifactTable);
        }

        // Summary
        var summaryTable = new Table { Title = new TableTitle("[bold]Discovery Summary[/]") };
        summaryTable.AddColumn("Metric");
        summaryTable.AddColumn("Count");

        summaryTable.AddRow("Total Bug Lists Found", allResults.Count.ToString());
        summaryTable.AddRow("From Paper Content", paperResults.Count.ToString());
        summaryTable.AddRow("From Artifact Repositories", artifactResults.Count.ToString());
        summaryTable.AddRow(
            "High Confidence (>0.8)",
            allResults.Count(r => r.Confidence > 0.8).ToString()
        );
        summaryTable.AddRow(
            "Medium Confidence (0.6-0.8)",
            allResults.Count(r => r.Confidence is > 0.6 and <= 0.8).ToString()
        );
        summaryTable.AddRow(
            "Low Confidence (≤0.6)",
            allResults.Count(r => r.Confidence <= 0.6).ToString()
        );

        AnsiConsole.Write(summaryTable);

        if (allResults.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No bug lists were discovered for this paper.[/]");
            AnsiConsole.MarkupLine("[dim]This could mean:[/]");
            AnsiConsole.MarkupLine("[dim]• The paper doesn't contain or reference bug lists[/]");
            AnsiConsole.MarkupLine("[dim]• Bug lists are in non-standard formats or locations[/]");
            AnsiConsole.MarkupLine("[dim]• Artifact repositories are private or inaccessible[/]");
        }
    }

    private async Task ExportComprehensiveResults(
        List<DiscoveryResult> allResults,
        List<DiscoveryResult> discoveredArtifacts,
        string? outputPath,
        string doi
    )
    {
        var finalPath =
            outputPath
            ?? Path.Combine(
                _pathsOptions.BaseDir,
                $"comprehensive-discovery-{doi.Replace("/", "_").Replace(":", "_")}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json"
            );

        var directory = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var exportData = new ComprehensiveDiscoveryExport
        {
            DOI = doi,
            Timestamp = DateTime.UtcNow,
            TotalResults = allResults.Count,
            DiscoveredArtifacts = discoveredArtifacts.Count,
            AllResults = allResults,
            Artifacts = discoveredArtifacts,
        };

        try
        {
            var json = JsonSerializer.Serialize(
                exportData,
                ExportModelJsonContext.Default.ComprehensiveDiscoveryExport
            );
            await File.WriteAllTextAsync(finalPath, json);
            AnsiConsole.MarkupLine(
                $"[green]Successfully exported comprehensive results to {finalPath}[/]"
            );
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error exporting comprehensive results to {OutputPath}: {Message}",
                finalPath,
                ex.Message
            );
            AnsiConsole.MarkupLine($"[red]Error exporting results:[/] {ex.Message}");
        }
    }

    private void LogComprehensiveDiscoveryCompletion(List<DiscoveryResult> allResults, string doi)
    {
        var paperResults = allResults
            .Where(r =>
                !r.Metadata.ContainsKey("source_location")
                || r.Metadata["source_location"] != "artifact_repository"
            )
            .Count();
        var artifactResults = allResults
            .Where(r =>
                r.Metadata.ContainsKey("source_location")
                && r.Metadata["source_location"] == "artifact_repository"
            )
            .Count();

        logger.LogInformation(
            "Comprehensive bug list discovery completed for DOI: {DOI}. Found {TotalResults} results: {PaperResults} from paper content, {ArtifactResults} from artifact repositories",
            doi,
            allResults.Count,
            paperResults,
            artifactResults
        );
    }

    private void LogAndDisplayError(string message)
    {
        logger.LogError(message);
        AnsiConsole.MarkupLine($"[red]Error:[/] {message}");
    }
}
