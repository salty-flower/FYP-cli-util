using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using DataCollection.Models;
using DataCollection.Models.Export;
using DataCollection.Options;
using DataCollection.Services;
using DataCollection.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace DataCollection.Commands.Repl;

/// <summary>
/// Metadata REPL command for testing keyword expressions against paper metadata
/// </summary>
public class MetadataReplCommand(
    ILogger<MetadataReplCommand> logger,
    IOptions<PathsOptions> pathsOptions,
    PdfDescriptionService pdfDescriptionService,
    DataLoadingService dataLoadingService
) : BaseReplCommand(logger)
{
    private readonly PathsOptions _pathsOptions = pathsOptions.Value;

    /// <summary>
    /// Run the Metadata REPL
    /// </summary>
    public void Run(CancellationToken cancellationToken = default)
    {
        var papers = dataLoadingService.LoadPapersFromMetadata(_pathsOptions.PaperMetadataDir);

        if (papers.Count == 0)
        {
            logger.LogWarning(
                "No paper metadata could be loaded. Please run the scrape papers command first."
            );
            return;
        }

        // Ask user if they want to inspect a single paper or work with all papers
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("How would you like to work with the [green]papers[/]?")
                .PageSize(10)
                .AddChoices(new[] { "Inspect a single paper", "Work with all papers" })
        );

        if (choice == "Inspect a single paper")
        {
            // Select a single paper to inspect
            var paperChoices = papers
                .Select(paper =>
                    ConsoleRenderingService.SafeMarkup(
                        pdfDescriptionService.GetItemDescription(paper)
                    )
                )
                .ToArray();

            var selectedPaperDesc = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select a [green]paper[/] to inspect")
                    .PageSize(20)
                    .AddChoices(paperChoices)
            );

            var selectedPaper = papers[Array.IndexOf(paperChoices, selectedPaperDesc)];
            RunSinglePaperRepl(selectedPaper, papers, cancellationToken);
        }
        else
        {
            // Work with all papers
            RunAllPapersRepl(papers, cancellationToken);
        }
    }

    /// <summary>
    /// Run REPL for a single paper
    /// </summary>
    private void RunSinglePaperRepl(
        Paper paper,
        List<Paper> allPapers,
        CancellationToken cancellationToken
    )
    {
        AnsiConsole.Clear();
        string safeTitle = ConsoleRenderingService.SafeMarkup(
            pdfDescriptionService.GetItemDescription(paper)
        );
        AnsiConsole.Write(new Rule($"Metadata REPL for [yellow]{safeTitle}[/]").RuleStyle("green"));

        var commands = new Dictionary<string, string>
        {
            { "count <keyword>", "Count occurrences of a keyword in title and abstract" },
            { "eval <expression>", "Evaluate a keyword expression against metadata" },
            { "showall [true|false]", "Toggle showing all results (no result limits)" },
            { "export [filename]", "Export last search results to JSON" },
            { "info", "Show paper information" },
            { "abstract", "Show the paper abstract" },
            { "title", "Show the paper title" },
            { "authors", "Show the paper authors" },
            { "exit", "Exit REPL" },
        };

        DisplayHelpTable(commands);

        while (!cancellationToken.IsCancellationRequested)
        {
            var input = AnsiConsole.Prompt(new TextPrompt<string>("> ").PromptStyle("green"));

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (input.Equals("exit", StringComparison.CurrentCultureIgnoreCase))
                break;

            if (input.Equals("info", StringComparison.CurrentCultureIgnoreCase))
            {
                DisplayPaperInfo(paper);
                continue;
            }

            if (input.Equals("abstract", StringComparison.CurrentCultureIgnoreCase))
            {
                DisplayPaperAbstract(paper);
                continue;
            }

            if (input.Equals("title", StringComparison.CurrentCultureIgnoreCase))
            {
                DisplayPaperTitle(paper);
                continue;
            }

            if (input.Equals("authors", StringComparison.CurrentCultureIgnoreCase))
            {
                DisplayPaperAuthors(paper);
                continue;
            }

            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            try
            {
                switch (parts[0].ToLowerInvariant())
                {
                    case "count":
                        HandleCountCommand(paper, parts);
                        break;
                    case "eval":
                        HandleEvalCommand(paper, input.Substring(5));
                        break;
                    case "showall":
                        HandleShowAllCommand(parts);
                        break;
                    case "export":
                        if (parts.Length == 1)
                        {
                            AnsiConsole.MarkupLine("[red]No export path provided[/]");
                            break;
                        }
                        HandleExportCommand(parts);
                        break;
                    default:
                        AnsiConsole.MarkupLine(
                            $"[red]Unknown command:[/] {ConsoleRenderingService.SafeMarkup(parts[0])}"
                        );
                        break;
                }
            }
            catch (Exception ex)
            {
                HandleError(ex, "single paper REPL");
            }
        }
    }

    /// <summary>
    /// Run REPL for all papers
    /// </summary>
    private void RunAllPapersRepl(List<Paper> papers, CancellationToken cancellationToken)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(
            new Rule(
                $"Metadata REPL for [yellow]ALL Papers[/] ({papers.Count} documents)"
            ).RuleStyle("green")
        );

        var commands = new Dictionary<string, string>
        {
            { "filter <expression>", "Filter papers with a keyword expression" },
            { "stats <keyword>", "Show statistics for a keyword across all papers" },
            { "rank <keyword>", "Rank papers by keyword occurrence" },
            { "search <pattern>", "Search for a pattern in paper metadata" },
            { "list", "List all available papers" },
            { "select <number>", "Select a specific paper to inspect" },
            { "showall [true|false]", "Toggle showing all results (no result limits)" },
            { "export [filename]", "Export last search results to JSON" },
            { "info", "Show summary information about all papers" },
            { "exit", "Exit REPL" },
        };

        DisplayHelpTable(commands);

        while (!cancellationToken.IsCancellationRequested)
        {
            var input = AnsiConsole.Prompt(new TextPrompt<string>("> ").PromptStyle("green"));

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (input.Equals("exit", StringComparison.CurrentCultureIgnoreCase))
                break;

            if (input.Equals("list", StringComparison.CurrentCultureIgnoreCase))
            {
                DisplayPapersList(papers);
                continue;
            }

            if (input.Equals("info", StringComparison.CurrentCultureIgnoreCase))
            {
                DisplayCollectionInfo(papers);
                continue;
            }

            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            try
            {
                switch (parts[0].ToLowerInvariant())
                {
                    case "filter":
                        HandleFilterCommand(papers, input.Substring(7));
                        break;
                    case "stats":
                        HandleStatsCommand(papers, parts);
                        break;
                    case "rank":
                        HandleRankCommand(papers, parts);
                        break;
                    case "search":
                        HandleSearchCommand(papers, parts);
                        break;
                    case "select":
                        if (HandleSelectCommand(papers, parts, cancellationToken))
                        {
                            // If select command returns true, user wants to return to the main REPL
                            return;
                        }
                        break;
                    case "showall":
                        HandleShowAllCommand(parts);
                        break;
                    case "export":
                        HandleExportCommand(parts);
                        break;
                    default:
                        AnsiConsole.MarkupLine(
                            $"[red]Unknown command:[/] {ConsoleRenderingService.SafeMarkup(parts[0])}"
                        );
                        break;
                }
            }
            catch (Exception ex)
            {
                HandleError(ex, "all papers REPL");
            }
        }
    }

    /// <summary>
    /// Display information about a single paper
    /// </summary>
    private static void DisplayPaperInfo(Paper paper)
    {
        AnsiConsole.Write(new Rule("Paper Information").RuleStyle("blue"));

        var table = new Table();
        table.AddColumn("Property");
        table.AddColumn("Value");

        table.AddRow("Title", ConsoleRenderingService.SafeMarkup(paper.Title));
        table.AddRow("DOI", ConsoleRenderingService.SafeMarkup(paper.Doi));
        table.AddRow("URL", ConsoleRenderingService.SafeMarkup(paper.Url));
        table.AddRow(
            "Authors",
            ConsoleRenderingService.SafeMarkup(string.Join(", ", paper.Authors))
        );
        table.AddRow("Abstract Length", paper.Abstract.Length.ToString());

        AnsiConsole.Write(table);
    }

    /// <summary>
    /// Display a paper's abstract
    /// </summary>
    private static void DisplayPaperAbstract(Paper paper)
    {
        AnsiConsole.Write(new Rule("Paper Abstract").RuleStyle("blue"));

        var panel = new Panel(ConsoleRenderingService.SafeMarkup(paper.Abstract))
        {
            Header = new PanelHeader("Abstract"),
            Expand = true,
            Border = BoxBorder.Rounded,
        };

        AnsiConsole.Write(panel);
    }

    /// <summary>
    /// Display a paper's title
    /// </summary>
    private static void DisplayPaperTitle(Paper paper)
    {
        AnsiConsole.Write(new Rule("Paper Title").RuleStyle("blue"));
        AnsiConsole.MarkupLine(ConsoleRenderingService.SafeMarkup(paper.Title));
    }

    /// <summary>
    /// Display a paper's authors
    /// </summary>
    private static void DisplayPaperAuthors(Paper paper)
    {
        AnsiConsole.Write(new Rule("Paper Authors").RuleStyle("blue"));

        var table = new Table();
        table.AddColumn("#");
        table.AddColumn("Author");

        for (int i = 0; i < paper.Authors.Length; i++)
        {
            table.AddRow((i + 1).ToString(), ConsoleRenderingService.SafeMarkup(paper.Authors[i]));
        }

        AnsiConsole.Write(table);
    }

    /// <summary>
    /// Display a list of all papers
    /// </summary>
    private static void DisplayPapersList(List<Paper> papers)
    {
        var table = new Table();
        table.AddColumn("#");
        table.AddColumn("Paper Title");
        table.AddColumn("DOI");

        for (int i = 0; i < papers.Count; i++)
        {
            table.AddRow(
                (i + 1).ToString(),
                ConsoleRenderingService.SafeMarkup(papers[i].Title),
                ConsoleRenderingService.SafeMarkup(papers[i].Doi)
            );
        }

        AnsiConsole.Write(new Rule($"Available Papers ({papers.Count})").RuleStyle("blue"));
        AnsiConsole.Write(table);
    }

    /// <summary>
    /// Display summary information about all papers
    /// </summary>
    private static void DisplayCollectionInfo(List<Paper> papers)
    {
        AnsiConsole.Write(new Rule("Paper Collection Summary").RuleStyle("blue"));

        // Calculate some stats
        int totalChars = papers.Sum(p => p.Abstract.Length);
        double avgAbstractLength = totalChars / (double)papers.Count;

        var table = new Table();
        table.AddColumn("Metric");
        table.AddColumn("Value");

        table.AddRow("Total Papers", papers.Count.ToString());
        table.AddRow("Total Abstract Characters", totalChars.ToString());
        table.AddRow("Average Abstract Length", avgAbstractLength.ToString("F0"));

        AnsiConsole.Write(table);
    }

    /// <summary>
    /// Count occurrences of keywords in a paper's metadata
    /// </summary>
    private static Dictionary<string, int> CountKeywords(Paper paper, params string[] keywords)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var keyword in keywords)
        {
            counts[keyword] = 0;
        }

        // Combine title and abstract for counting
        string text = paper.Title + " " + paper.Abstract;

        foreach (var keyword in keywords)
        {
            // Use regex to count occurrences (whole word matching, case insensitive)
            var matches = Regex.Matches(
                text,
                $@"\b{Regex.Escape(keyword)}\b",
                RegexOptions.IgnoreCase
            );
            counts[keyword] = matches.Count;
        }

        return counts;
    }

    /// <summary>
    /// Handle the count command
    /// </summary>
    private void HandleCountCommand(Paper paper, string[] parts)
    {
        if (parts.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] count <keyword>");
            return;
        }

        string keyword = parts[1].ToLower();
        var counts = CountKeywords(paper, keyword);

        var table = new Table();
        table.AddColumn("Keyword");
        table.AddColumn("Count");

        table.AddRow(keyword, counts[keyword].ToString());

        AnsiConsole.Write(new Rule($"Keyword counts for [blue]{keyword}[/]").RuleStyle("green"));
        AnsiConsole.Write(table);
    }

    /// <summary>
    /// Handle the eval command
    /// </summary>
    private void HandleEvalCommand(Paper paper, string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] eval <expression>");
            return;
        }

        expression = expression.Trim();

        try
        {
            // Extract keywords from the expression
            var keywords = ExtractKeywordsFromExpression(expression);
            if (keywords.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No keywords found in expression[/]");
                return;
            }

            var counts = CountKeywords(paper, keywords.ToArray());

            // Parse and evaluate the expression
            var expressionFunc = KeywordExpressionParser.ParseExpression(expression);
            bool result = expressionFunc(counts);

            // Display results
            AnsiConsole.Write(
                new Rule(
                    $"Evaluation of [blue]{ConsoleRenderingService.SafeMarkup(expression)}[/]"
                ).RuleStyle("green")
            );

            var table = new Table();
            table.AddColumn("Keyword");
            table.AddColumn("Count");

            foreach (var keyword in keywords.OrderByDescending(k => counts[k]).ThenBy(k => k))
            {
                table.AddRow(keyword, counts[keyword].ToString());
            }

            AnsiConsole.Write(table);

            AnsiConsole.MarkupLine($"Expression result: [{(result ? "green" : "red")}]{result}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine(
                $"[red]Error evaluating expression:[/] {ConsoleRenderingService.SafeMarkup(ex.Message)}"
            );
        }
    }

    /// <summary>
    /// Handle the showall command
    /// </summary>
    private void HandleShowAllCommand(string[] parts)
    {
        if (parts.Length > 1 && bool.TryParse(parts[1], out bool value))
        {
            ShowAllResults = value;
            AnsiConsole.MarkupLine(
                $"Set showing all results to: [{(ShowAllResults ? "green" : "red")}]{ShowAllResults}[/]"
            );
        }
        else
        {
            // Toggle current value
            ShowAllResults = !ShowAllResults;
            AnsiConsole.MarkupLine(
                $"Toggled showing all results to: [{(ShowAllResults ? "green" : "red")}]{ShowAllResults}[/]"
            );
        }
    }

    /// <summary>
    /// Handle the export command
    /// </summary>
    protected bool HandleExportCommand(string[] parts)
    {
        string filename = parts[1];

        if (LastSearchResults == null)
        {
            AnsiConsole.MarkupLine("[red]No data available to export[/]");
            return false;
        }

        try
        {
            // Determine the type of the last search results and use appropriate source generation
            if (LastSearchResults is MetadataSearchResult searchResult)
            {
                return WriteToFile(
                    searchResult,
                    filename,
                    ReplJsonContext.Default.MetadataSearchResult
                );
            }
            else if (LastSearchResults is MetadataEvaluationResult evaluationResult)
            {
                return WriteToFile(
                    evaluationResult,
                    filename,
                    ReplJsonContext.Default.MetadataEvaluationResult
                );
            }
            else
            {
                AnsiConsole.MarkupLine(
                    $"[red]Unknown data type for export:[/] {LastSearchResults.GetType()}"
                );
                return false;
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine(
                $"[red]Error exporting data:[/] {ConsoleRenderingService.SafeMarkup(ex.Message)}"
            );
            return false;
        }
    }

    /// <summary>
    /// Run non-interactive search across all papers and optionally export results
    /// </summary>
    /// <param name="pattern">Search pattern (regex supported)</param>
    /// <param name="results">Out parameter to store the search results</param>
    /// <param name="exportPath">Optional path to export results</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of results found</returns>
    public int RunNonInteractiveSearch(
        string pattern,
        out MetadataSearchResult results,
        string exportPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var papers = dataLoadingService.LoadPapersFromMetadata(_pathsOptions.PaperMetadataDir);

        if (papers.Count == 0)
        {
            logger.LogWarning(
                "No paper metadata could be loaded. Please run the scrape papers command first."
            );
            results = null;
            return 0;
        }

        logger.LogInformation(
            "Searching for pattern '{Pattern}' in {Count} papers...",
            pattern,
            papers.Count
        );

        try
        {
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            var searchResults = new List<(Paper Paper, string Match, string Context)>();

            foreach (var paper in papers)
            {
                // Search in title
                var titleMatches = regex.Matches(paper.Title);
                foreach (Match match in titleMatches)
                {
                    searchResults.Add((paper, match.Value, $"Title: {paper.Title}"));
                }

                // Search in abstract
                var abstractMatches = regex.Matches(paper.Abstract);
                foreach (Match match in abstractMatches)
                {
                    // Get some context around the match
                    int start = Math.Max(0, match.Index - 40);
                    int length = Math.Min(paper.Abstract.Length - start, match.Length + 80);
                    string context = paper.Abstract.Substring(start, length);

                    searchResults.Add((paper, match.Value, $"Abstract: ...{context}..."));
                }
            }

            // Create properly structured data for source generation
            results = new MetadataSearchResult
            {
                Pattern = pattern,
                TotalMatches = searchResults.Count,
                Timestamp = DateTime.Now,
                Results = searchResults
                    .Select(r => new MetadataSearchItem
                    {
                        Paper = new PaperReference
                        {
                            Title = r.Paper.Title,
                            DOI = r.Paper.Doi,
                            Authors = r.Paper.Authors?.ToArray() ?? Array.Empty<string>(),
                        },
                        Match = r.Match,
                        Context = r.Context,
                    })
                    .ToList(),
            };

            // Store the data for potential later use
            LastSearchResults = results;

            // Export if path is provided or log the results
            if (!string.IsNullOrEmpty(exportPath))
            {
                if (WriteToFile(results, exportPath, ReplJsonContext.Default.MetadataSearchResult))
                {
                    logger.LogInformation(
                        "Exported {Count} results to {Path}",
                        searchResults.Count,
                        exportPath
                    );
                }
                else
                {
                    logger.LogError("Failed to export results to {Path}", exportPath);
                }
            }
            else
            {
                if (searchResults.Count > 0)
                {
                    logger.LogInformation(
                        "Found {Count} matches in paper metadata. No export path provided.",
                        searchResults.Count
                    );

                    // Log a sample of the results
                    foreach (var result in searchResults.Take(5))
                    {
                        logger.LogInformation(
                            "Match in {Paper}: {Match}",
                            result.Paper.Title,
                            result.Match
                        );
                    }

                    if (searchResults.Count > 5)
                    {
                        logger.LogInformation(
                            "... and {Count} more matches",
                            searchResults.Count - 5
                        );
                    }
                }
                else
                {
                    logger.LogInformation("No matches found for pattern '{Pattern}'", pattern);
                }
            }

            return searchResults.Count;
        }
        catch (RegexParseException ex)
        {
            logger.LogError("Invalid regex pattern: {Error}", ex.Message);
            results = null;
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during non-interactive search: {Error}", ex.Message);
            results = null;
            return 0;
        }
    }

    /// <summary>
    /// Run non-interactive evaluation of an expression across all papers
    /// </summary>
    /// <param name="expression">Keyword expression to evaluate</param>
    /// <param name="exportPath">Optional path to export results</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of papers matching the expression</returns>
    public int RunNonInteractiveEvaluation(
        string expression,
        string exportPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var papers = dataLoadingService.LoadPapersFromMetadata(_pathsOptions.PaperMetadataDir);

        if (papers.Count == 0)
        {
            logger.LogWarning(
                "No paper metadata could be loaded. Please run the scrape papers command first."
            );
            return 0;
        }

        logger.LogInformation(
            "Evaluating expression '{Expression}' across {Count} papers...",
            expression,
            papers.Count
        );

        try
        {
            // Extract keywords from the expression
            var keywords = ExtractKeywordsFromExpression(expression);
            if (keywords.Count == 0)
            {
                logger.LogWarning("No keywords found in expression");
                return 0;
            }

            // Parse the expression
            var expressionFunc = KeywordExpressionParser.ParseExpression(expression);

            // Filter papers
            var matchingPapers = new List<(Paper Paper, Dictionary<string, int> Counts)>();

            foreach (var paper in papers)
            {
                var counts = CountKeywords(paper, keywords.ToArray());
                if (expressionFunc(counts))
                {
                    matchingPapers.Add((paper, counts));
                }
            }

            // Create properly structured data for source generation
            var exportData = new MetadataEvaluationResult
            {
                Expression = expression,
                TotalMatches = matchingPapers.Count,
                Timestamp = DateTime.Now,
                MatchingPapers = matchingPapers
                    .Select(p => new MetadataEvaluationItem
                    {
                        Paper = new PaperReference
                        {
                            Title = p.Paper.Title,
                            DOI = p.Paper.Doi,
                            Authors = p.Paper.Authors?.ToArray() ?? Array.Empty<string>(),
                        },
                        KeywordCounts = p.Counts,
                    })
                    .ToList(),
            };

            // Store the data for potential later use
            LastSearchResults = exportData;

            // Export if path is provided or log the results
            if (!string.IsNullOrEmpty(exportPath))
            {
                if (
                    WriteToFile(
                        exportData,
                        exportPath,
                        ReplJsonContext.Default.MetadataEvaluationResult
                    )
                )
                {
                    logger.LogInformation(
                        "Exported {Count} matching papers to {Path}",
                        matchingPapers.Count,
                        exportPath
                    );
                }
                else
                {
                    logger.LogWarning("Failed to export results to {Path}", exportPath);
                }
            }
            else
            {
                logger.LogInformation(
                    "{Count} papers matched the expression",
                    matchingPapers.Count
                );
            }

            return matchingPapers.Count;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error evaluating expression: {Message}", ex.Message);
            return 0;
        }
    }

    /// <summary>
    /// Handle the filter command
    /// </summary>
    private void HandleFilterCommand(List<Paper> papers, string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] filter <expression>");
            return;
        }

        expression = expression.Trim();

        try
        {
            // Extract keywords from the expression
            var keywords = ExtractKeywordsFromExpression(expression);
            if (keywords.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No keywords found in expression[/]");
                return;
            }

            // Parse the expression
            var expressionFunc = KeywordExpressionParser.ParseExpression(expression);

            // Filter papers
            var matchingPapers = new List<(Paper Paper, Dictionary<string, int> Counts)>();

            foreach (var paper in papers)
            {
                var counts = CountKeywords(paper, keywords.ToArray());
                if (expressionFunc(counts))
                {
                    matchingPapers.Add((paper, counts));
                }
            }

            // Display results
            if (matchingPapers.Count == 0)
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]No papers match[/] the expression: '{ConsoleRenderingService.SafeMarkup(expression)}'"
                );
                return;
            }

            AnsiConsole.Write(
                new Rule(
                    $"Found {matchingPapers.Count} papers matching: '{ConsoleRenderingService.SafeMarkup(expression)}'"
                ).RuleStyle("green")
            );

            var table = new Table();
            table.AddColumn("#");
            table.AddColumn("Paper Title");
            table.AddColumn("Key Counts");

            for (int i = 0; i < matchingPapers.Count; i++)
            {
                var (paper, counts) = matchingPapers[i];

                // Get the most relevant counts to display
                var countSummary = string.Join(
                    ", ",
                    counts
                        .Where(c => c.Value > 0)
                        .OrderByDescending(c => c.Value)
                        .Take(3)
                        .Select(c => $"{c.Key}: {c.Value}")
                );

                table.AddRow(
                    (i + 1).ToString(),
                    ConsoleRenderingService.SafeMarkup(paper.Title),
                    countSummary
                );
            }

            AnsiConsole.Write(table);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine(
                $"[red]Error filtering papers:[/] {ConsoleRenderingService.SafeMarkup(ex.Message)}"
            );
        }
    }

    /// <summary>
    /// Handle the select command
    /// </summary>
    private bool HandleSelectCommand(
        List<Paper> papers,
        string[] parts,
        CancellationToken cancellationToken
    )
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out int paperIndex))
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] select <number>");
            return false;
        }

        // Adjust for 1-based indexing that users see
        paperIndex--;

        if (paperIndex < 0 || paperIndex >= papers.Count)
        {
            AnsiConsole.MarkupLine($"[red]Invalid paper number.[/] Valid range: 1-{papers.Count}");
            return false;
        }

        var selectedPaper = papers[paperIndex];
        string safeTitle = ConsoleRenderingService.SafeMarkup(selectedPaper.Title);

        AnsiConsole.MarkupLine($"Selected [yellow]{safeTitle}[/]");
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .AddChoices(
                    new[] { "Inspect this paper in detail", "Cancel and return to all papers mode" }
                )
        );

        if (choice == "Inspect this paper in detail")
        {
            RunSinglePaperRepl(selectedPaper, papers, cancellationToken);

            // Ask if the user wants to return to all papers mode or exit
            return AnsiConsole.Confirm("Return to all papers mode?");
        }

        return false;
    }

    /// <summary>
    /// Handle the stats command
    /// </summary>
    private void HandleStatsCommand(List<Paper> papers, string[] parts)
    {
        if (parts.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] stats <keyword>");
            return;
        }

        string keyword = parts[1].ToLower();
        var allCounts = new List<(Paper Paper, int Count)>();

        foreach (var paper in papers)
        {
            var counts = CountKeywords(paper, keyword);
            allCounts.Add((paper, counts[keyword]));
        }

        // Calculate statistics
        int total = allCounts.Sum(c => c.Count);
        double average = allCounts.Average(c => c.Count);
        int max = allCounts.Max(c => c.Count);
        int min = allCounts.Min(c => c.Count);
        int median = allCounts.OrderBy(c => c.Count).ElementAt(allCounts.Count / 2).Count;
        int papersWithKeyword = allCounts.Count(c => c.Count > 0);

        // Display statistics
        AnsiConsole.Write(
            new Rule($"Statistics for keyword: [blue]{keyword}[/]").RuleStyle("green")
        );

        var statsTable = new Table();
        statsTable.AddColumn("Statistic");
        statsTable.AddColumn("Value");

        statsTable.AddRow("Total occurrences", total.ToString());
        statsTable.AddRow("Average per paper", average.ToString("F2"));
        statsTable.AddRow("Maximum in a paper", max.ToString());
        statsTable.AddRow("Minimum in a paper", min.ToString());
        statsTable.AddRow("Median", median.ToString());
        statsTable.AddRow(
            "Papers with keyword",
            $"{papersWithKeyword} ({(double)papersWithKeyword / papers.Count:P0})"
        );

        AnsiConsole.Write(statsTable);
    }

    /// <summary>
    /// Handle the rank command
    /// </summary>
    private void HandleRankCommand(List<Paper> papers, string[] parts)
    {
        if (parts.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] rank <keyword>");
            return;
        }

        string keyword = parts[1].ToLower();
        var allCounts = new List<(Paper Paper, int Count)>();

        foreach (var paper in papers)
        {
            var counts = CountKeywords(paper, keyword);
            allCounts.Add((paper, counts[keyword]));
        }

        // Sort by count (descending)
        allCounts = allCounts.OrderByDescending(c => c.Count).ToList();

        // Display results
        AnsiConsole.Write(
            new Rule($"Papers ranked by occurrences of: [blue]{keyword}[/]").RuleStyle("green")
        );

        if (allCounts.All(c => c.Count == 0))
        {
            AnsiConsole.MarkupLine($"[yellow]No occurrences of '{keyword}' found in any paper[/]");
            return;
        }

        var table = new Table();
        table.AddColumn("Rank");
        table.AddColumn("Paper Title");
        table.AddColumn("Count");

        for (int i = 0; i < allCounts.Count && i < 20; i++)
        {
            var (paper, count) = allCounts[i];

            if (count == 0)
                continue;

            table.AddRow(
                (i + 1).ToString(),
                ConsoleRenderingService.SafeMarkup(paper.Title),
                count.ToString()
            );
        }

        AnsiConsole.Write(table);

        if (allCounts.Count > 20 && allCounts[20].Count > 0)
        {
            AnsiConsole.MarkupLine(
                $"[grey]... and {allCounts.Count(c => c.Count > 0) - 20} more papers with occurrences[/]"
            );
        }
    }

    /// <summary>
    /// Handle the search command
    /// </summary>
    private void HandleSearchCommand(List<Paper> papers, string[] parts)
    {
        if (parts.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] search <pattern>");
            return;
        }

        string pattern = string.Join(" ", parts.Skip(1));

        try
        {
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            var results = new List<(Paper Paper, string Match, string Context)>();

            foreach (var paper in papers)
            {
                // Search in title
                var titleMatches = regex.Matches(paper.Title);
                foreach (Match match in titleMatches)
                {
                    results.Add((paper, match.Value, $"Title: {paper.Title}"));
                }

                // Search in abstract
                var abstractMatches = regex.Matches(paper.Abstract);
                foreach (Match match in abstractMatches)
                {
                    // Get some context around the match
                    int start = Math.Max(0, match.Index - 40);
                    int length = Math.Min(paper.Abstract.Length - start, match.Length + 80);
                    string context = paper.Abstract.Substring(start, length);

                    results.Add((paper, match.Value, $"Abstract: ...{context}..."));

                    // Limit the number of results per paper if not showing all
                    if (!ShowAllResults && results.Count >= 100)
                        break;
                }

                // Limit total results if not showing all
                if (!ShowAllResults && results.Count >= 100)
                    break;
            }

            // Store results for potential export
            LastSearchResults = new
            {
                Pattern = pattern,
                TotalMatches = results.Count,
                Timestamp = DateTime.Now,
                Results = results
                    .Select(r => new
                    {
                        Paper = new
                        {
                            Title = r.Paper.Title,
                            DOI = r.Paper.Doi,
                            Authors = r.Paper.Authors ?? new string[0],
                        },
                        Match = r.Match,
                        Context = r.Context,
                    })
                    .ToList(),
            };

            // Display results
            if (results.Count == 0)
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]No matches found[/] for '{ConsoleRenderingService.SafeMarkup(pattern)}'"
                );
                return;
            }

            AnsiConsole.Write(
                new Rule(
                    $"Found {results.Count} matches for '{ConsoleRenderingService.SafeMarkup(pattern)}'"
                ).RuleStyle("green")
            );

            var table = new Table();
            table.AddColumn("Paper");
            table.AddColumn("Match");
            table.AddColumn("Context");

            // Determine how many results to show
            int showCount = ShowAllResults ? results.Count : Math.Min(50, results.Count);

            foreach (var (paper, match, context) in results.Take(showCount))
            {
                table.AddRow(
                    ConsoleRenderingService.SafeMarkup(paper.Title),
                    ConsoleRenderingService.SafeMarkup(match),
                    ConsoleRenderingService.SafeMarkup(context)
                );
            }

            AnsiConsole.Write(table);

            if (!ShowAllResults && results.Count > 50)
            {
                AnsiConsole.MarkupLine(
                    $"[grey]... and {results.Count - 50} more matches[/] (use 'showall true' to see all)"
                );
            }
        }
        catch (RegexParseException ex)
        {
            AnsiConsole.MarkupLine(
                $"[red]Invalid regex pattern:[/] {ConsoleRenderingService.SafeMarkup(ex.Message)}"
            );
        }
    }

    /// <summary>
    /// Extracts keywords from an expression
    /// </summary>
    private static HashSet<string> ExtractKeywordsFromExpression(string expression)
    {
        // Simple extraction logic - this could be enhanced
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Remove operators and parentheses
        var cleaned = expression
            .Replace("(", " ")
            .Replace(")", " ")
            .Replace(">", " ")
            .Replace("<", " ")
            .Replace("=", " ")
            .Replace("AND", " ")
            .Replace("OR", " ")
            .Replace("NOT", " ");

        // Split by spaces and extract potential keywords
        foreach (var part in cleaned.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            // If not a number, it might be a keyword
            if (!int.TryParse(part, out _))
            {
                keywords.Add(part.Trim());
            }
        }

        return keywords;
    }
}
