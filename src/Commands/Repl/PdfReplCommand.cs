using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using DataCollection.Models;
using DataCollection.Options;
using DataCollection.Services;
using DataCollection.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace DataCollection.Commands.Repl;

/// <summary>
/// PDF REPL command for testing keyword expressions against PDF content
/// </summary>
public class PdfReplCommand(
    ILogger<PdfReplCommand> logger,
    IOptions<PathsOptions> pathsOptions,
    IOptions<KeywordsOptions> keywordsOptions,
    PdfDescriptionService pdfDescriptionService,
    ConsoleRenderingService renderingService,
    PdfSearchService searchService,
    DataLoadingService dataLoadingService,
    JsonExportService jsonExportService
) : BaseReplCommand(logger, jsonExportService)
{
    private readonly PathsOptions _pathsOptions = pathsOptions.Value;
    private readonly KeywordsOptions _keywordsOptions = keywordsOptions.Value;

    /// <summary>
    /// Run the PDF REPL
    /// </summary>
    public void Run(CancellationToken cancellationToken = default)
    {
        var pdfDataList = dataLoadingService.LoadPdfDataFromDirectory(
            _pathsOptions.PdfDataDir,
            _pathsOptions.PaperMetadataDir
        );

        if (pdfDataList.Count == 0)
        {
            logger.LogWarning(
                "No PDF data could be loaded. Please run the analyze pdfs command first."
            );
            return;
        }

        // Ask user if they want to work with a single PDF or all PDFs
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("How would you like to work with the [green]PDFs[/]?")
                .PageSize(10)
                .AddChoices(new[] { "Work with a single PDF", "Work with all PDFs" })
        );

        if (choice == "Work with a single PDF")
        {
            // Select a single PDF to work with
            var pdfChoices = pdfDataList
                .Select(pdf =>
                    ConsoleRenderingService.SafeMarkup(
                        pdfDescriptionService.GetItemDescription(pdf)
                    )
                )
                .ToArray();

            var selectedPdfDesc = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select a [green]PDF[/] to work with")
                    .PageSize(20)
                    .AddChoices(pdfChoices)
            );

            var selectedPdf = pdfDataList[Array.IndexOf(pdfChoices, selectedPdfDesc)];
            RunSinglePdfRepl(selectedPdf, cancellationToken);
        }
        else
        {
            // Work with all PDFs
            RunAllPdfsRepl(pdfDataList, cancellationToken);
        }
    }

    /// <summary>
    /// Run REPL for a single PDF
    /// </summary>
    private void RunSinglePdfRepl(PdfData pdfData, CancellationToken cancellationToken)
    {
        AnsiConsole.Clear();
        string safeTitle = ConsoleRenderingService.SafeMarkup(
            pdfDescriptionService.GetItemDescription(pdfData)
        );
        AnsiConsole.Write(new Rule($"PDF REPL for [yellow]{safeTitle}[/]").RuleStyle("green"));

        var commands = new Dictionary<string, string>
        {
            { "count <keyword>", "Count occurrences of a keyword" },
            { "countall", "Count occurrences of all predefined keywords" },
            { "eval <expression>", "Evaluate a keyword expression" },
            { "predefined", "Show predefined expressions from config" },
            { "evaluate <index>", "Evaluate a predefined expression" },
            { "search <pattern>", "Search for a pattern (case-insensitive regex)" },
            { "showall [true|false]", "Toggle showing all results (no result limits)" },
            { "info", "Show document information" },
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
                renderingService.DisplayPdfInfo(pdfData);
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
                        HandleCountCommand(pdfData, parts);
                        break;
                    case "countall":
                        HandleCountAllCommand(pdfData);
                        break;
                    case "eval":
                        HandleEvalCommand(pdfData, input.Substring(5));
                        break;
                    case "predefined":
                        DisplayPredefinedExpressions();
                        break;
                    case "evaluate":
                        HandleEvaluatePredefinedCommand(pdfData, parts);
                        break;
                    case "search":
                        HandleSearchCommand(pdfData, parts);
                        break;
                    case "showall":
                        HandleShowAllCommand(parts);
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
                HandleError(ex, "single PDF REPL");
            }
        }
    }

    /// <summary>
    /// Run REPL for all PDFs
    /// </summary>
    private void RunAllPdfsRepl(List<PdfData> allPdfData, CancellationToken cancellationToken)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(
            new Rule($"PDF REPL for [yellow]ALL PDFs[/] ({allPdfData.Count} documents)").RuleStyle(
                "green"
            )
        );

        var commands = new Dictionary<string, string>
        {
            { "filter <expression>", "Filter PDFs with a keyword expression" },
            { "stats <keyword>", "Show statistics for a keyword across all PDFs" },
            { "rank <keyword>", "Rank PDFs by keyword occurrence" },
            { "list", "List all available PDFs" },
            { "select <number>", "Select a specific PDF to work with" },
            { "showall [true|false]", "Toggle showing all results (no result limits)" },
            { "info", "Show summary information about all PDFs" },
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
                renderingService.DisplayPdfList(allPdfData);
                continue;
            }

            if (input.Equals("info", StringComparison.CurrentCultureIgnoreCase))
            {
                renderingService.DisplayPdfCollectionInfo(allPdfData);
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
                        HandleFilterCommand(allPdfData, input.Substring(7));
                        break;
                    case "stats":
                        HandleStatsCommand(allPdfData, parts);
                        break;
                    case "rank":
                        HandleRankCommand(allPdfData, parts);
                        break;
                    case "select":
                        if (HandleSelectCommand(allPdfData, parts, cancellationToken))
                        {
                            // If select command returns true, user wants to return to the main REPL
                            return;
                        }
                        break;
                    case "showall":
                        HandleShowAllCommand(parts);
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
                HandleError(ex, "all PDFs REPL");
            }
        }
    }

    /// <summary>
    /// Count occurrences of a keyword in a PDF
    /// </summary>
    private Dictionary<string, int> CountKeywords(PdfData pdfData, params string[] keywords)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var keyword in keywords)
        {
            counts[keyword] = 0;
        }

        if (pdfData.Texts != null)
        {
            foreach (var text in pdfData.Texts)
            {
                if (string.IsNullOrEmpty(text))
                    continue;

                foreach (var keyword in keywords)
                {
                    // Use regex to count occurrences (whole word matching, case insensitive)
                    var matches = Regex.Matches(
                        text,
                        $@"\b{Regex.Escape(keyword)}\b",
                        RegexOptions.IgnoreCase
                    );
                    counts[keyword] += matches.Count;
                }
            }
        }

        return counts;
    }

    /// <summary>
    /// Handle the count command
    /// </summary>
    private void HandleCountCommand(PdfData pdfData, string[] parts)
    {
        if (parts.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] count <keyword>");
            return;
        }

        string keyword = parts[1].ToLower();
        var counts = CountKeywords(pdfData, keyword);

        var table = new Table();
        table.AddColumn("Keyword");
        table.AddColumn("Count");

        table.AddRow(keyword, counts[keyword].ToString());

        AnsiConsole.Write(new Rule($"Keyword counts for [blue]{keyword}[/]").RuleStyle("green"));
        AnsiConsole.Write(table);
    }

    /// <summary>
    /// Handle the countall command
    /// </summary>
    private void HandleCountAllCommand(PdfData pdfData)
    {
        // Get all analysis keywords from options
        var keywords = _keywordsOptions.Analysis;

        if (keywords == null || keywords.Length == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No analysis keywords defined in configuration[/]");
            return;
        }

        var counts = CountKeywords(pdfData, keywords);

        var table = new Table();
        table.AddColumn("Keyword");
        table.AddColumn("Count");

        foreach (var keyword in keywords.OrderBy(k => k))
        {
            table.AddRow(keyword, counts[keyword].ToString());
        }

        AnsiConsole.Write(new Rule("Keyword counts for all analysis keywords").RuleStyle("green"));
        AnsiConsole.Write(table);
    }

    /// <summary>
    /// Handle the eval command
    /// </summary>
    private void HandleEvalCommand(PdfData pdfData, string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] eval <expression>");
            return;
        }

        expression = expression.Trim();

        try
        {
            // Get all keywords used in the expression
            // This is a simplified approach - a proper implementation would parse the expression
            // to extract keywords, but for now we'll just use all analysis keywords
            var keywords = _keywordsOptions.Analysis;
            var counts = CountKeywords(pdfData, keywords);

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

            foreach (var keyword in keywords.OrderBy(k => k))
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
    /// Display predefined expressions from configuration
    /// </summary>
    private void DisplayPredefinedExpressions()
    {
        var expressions = _keywordsOptions.ExpressionRules;

        if (expressions == null || expressions.Length == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No expression rules defined in configuration[/]");
            return;
        }

        var table = new Table();
        table.AddColumn("#");
        table.AddColumn("Expression");

        for (int i = 0; i < expressions.Length; i++)
        {
            table.AddRow((i + 1).ToString(), ConsoleRenderingService.SafeMarkup(expressions[i]));
        }

        AnsiConsole.Write(new Rule("Predefined Expressions").RuleStyle("green"));
        AnsiConsole.Write(table);
    }

    /// <summary>
    /// Handle the evaluate predefined command
    /// </summary>
    private void HandleEvaluatePredefinedCommand(PdfData pdfData, string[] parts)
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out int index))
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] evaluate <index>");
            return;
        }

        var expressions = _keywordsOptions.ExpressionRules;

        if (expressions == null || expressions.Length == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No expression rules defined in configuration[/]");
            return;
        }

        // Adjust for 1-based indexing
        index--;

        if (index < 0 || index >= expressions.Length)
        {
            AnsiConsole.MarkupLine(
                $"[red]Invalid expression index.[/] Valid range: 1-{expressions.Length}"
            );
            return;
        }

        string expression = expressions[index];
        HandleEvalCommand(pdfData, expression);
    }

    /// <summary>
    /// Handle the search command
    /// </summary>
    private void HandleSearchCommand(PdfData pdfData, string[] parts)
    {
        if (parts.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] search <pattern>");
            return;
        }

        // Get the pattern
        string pattern = searchService.ParseSearchPattern(parts);

        try
        {
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);

            var results = new List<(int PageIdx, string Text, string Match)>();

            // Search in full text
            if (pdfData.Texts != null)
            {
                for (int i = 0; i < pdfData.Texts.Length; i++)
                {
                    var text = pdfData.Texts[i];
                    if (string.IsNullOrEmpty(text))
                        continue;

                    var matches = regex.Matches(text);
                    foreach (Match match in matches)
                    {
                        // Get some context around the match
                        int start = Math.Max(0, match.Index - 40);
                        int length = Math.Min(text.Length - start, match.Length + 80);
                        string context = text.Substring(start, length);

                        results.Add((i, context, match.Value));

                        // Limit the number of results if not showing all
                        if (!ShowAllResults && results.Count >= 50)
                            break;
                    }

                    if (!ShowAllResults && results.Count >= 50)
                        break;
                }
            }

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
            table.AddColumn("Page");
            table.AddColumn("Context");

            // Determine how many results to show
            int showCount = ShowAllResults ? results.Count : Math.Min(50, results.Count);

            foreach (var result in results.Take(showCount))
            {
                string context = ConsoleRenderingService.SafeMarkup(result.Text);
                table.AddRow((result.PageIdx + 1).ToString(), context);
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
    /// Handle the filter command
    /// </summary>
    private void HandleFilterCommand(List<PdfData> allPdfData, string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] filter <expression>");
            return;
        }

        expression = expression.Trim();

        try
        {
            // Get all keywords used in the expression
            var keywords = _keywordsOptions.Analysis;

            // Parse the expression
            var expressionFunc = KeywordExpressionParser.ParseExpression(expression);

            // Filter PDFs
            var matchingPdfs = new List<(PdfData Pdf, Dictionary<string, int> Counts)>();

            foreach (var pdf in allPdfData)
            {
                var counts = CountKeywords(pdf, keywords);
                if (expressionFunc(counts))
                {
                    matchingPdfs.Add((pdf, counts));
                }
            }

            // Display results
            if (matchingPdfs.Count == 0)
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]No PDFs match[/] the expression: '{ConsoleRenderingService.SafeMarkup(expression)}'"
                );
                return;
            }

            AnsiConsole.Write(
                new Rule(
                    $"Found {matchingPdfs.Count} PDFs matching: '{ConsoleRenderingService.SafeMarkup(expression)}'"
                ).RuleStyle("green")
            );

            var table = new Table();
            table.AddColumn("#");
            table.AddColumn("PDF Name");
            table.AddColumn("Key Counts");

            for (int i = 0; i < matchingPdfs.Count; i++)
            {
                var (pdf, counts) = matchingPdfs[i];

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
                    ConsoleRenderingService.SafeMarkup(
                        pdfDescriptionService.GetItemDescription(pdf)
                    ),
                    countSummary
                );
            }

            AnsiConsole.Write(table);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine(
                $"[red]Error filtering PDFs:[/] {ConsoleRenderingService.SafeMarkup(ex.Message)}"
            );
        }
    }

    /// <summary>
    /// Handle the stats command
    /// </summary>
    private void HandleStatsCommand(List<PdfData> allPdfData, string[] parts)
    {
        if (parts.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] stats <keyword>");
            return;
        }

        string keyword = parts[1].ToLower();
        var allCounts = new List<(PdfData Pdf, int Count)>();

        foreach (var pdf in allPdfData)
        {
            var counts = CountKeywords(pdf, keyword);
            allCounts.Add((pdf, counts[keyword]));
        }

        // Calculate statistics
        int total = allCounts.Sum(c => c.Count);
        double average = allCounts.Average(c => c.Count);
        int max = allCounts.Max(c => c.Count);
        int min = allCounts.Min(c => c.Count);
        int median = allCounts.OrderBy(c => c.Count).ElementAt(allCounts.Count / 2).Count;
        int pdfsWithKeyword = allCounts.Count(c => c.Count > 0);

        // Display statistics
        AnsiConsole.Write(
            new Rule($"Statistics for keyword: [blue]{keyword}[/]").RuleStyle("green")
        );

        var statsTable = new Table();
        statsTable.AddColumn("Statistic");
        statsTable.AddColumn("Value");

        statsTable.AddRow("Total occurrences", total.ToString());
        statsTable.AddRow("Average per PDF", average.ToString("F2"));
        statsTable.AddRow("Maximum in a PDF", max.ToString());
        statsTable.AddRow("Minimum in a PDF", min.ToString());
        statsTable.AddRow("Median", median.ToString());
        statsTable.AddRow(
            "PDFs with keyword",
            $"{pdfsWithKeyword} ({(double)pdfsWithKeyword / allPdfData.Count:P0})"
        );

        AnsiConsole.Write(statsTable);
    }

    /// <summary>
    /// Handle the rank command
    /// </summary>
    private void HandleRankCommand(List<PdfData> allPdfData, string[] parts)
    {
        if (parts.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] rank <keyword>");
            return;
        }

        string keyword = parts[1].ToLower();
        var allCounts = new List<(PdfData Pdf, int Count)>();

        foreach (var pdf in allPdfData)
        {
            var counts = CountKeywords(pdf, keyword);
            allCounts.Add((pdf, counts[keyword]));
        }

        // Sort by count (descending)
        allCounts = allCounts.OrderByDescending(c => c.Count).ToList();

        // Display results
        AnsiConsole.Write(
            new Rule($"PDFs ranked by occurrences of: [blue]{keyword}[/]").RuleStyle("green")
        );

        if (allCounts.All(c => c.Count == 0))
        {
            AnsiConsole.MarkupLine($"[yellow]No occurrences of '{keyword}' found in any PDF[/]");
            return;
        }

        var table = new Table();
        table.AddColumn("Rank");
        table.AddColumn("PDF Name");
        table.AddColumn("Count");

        for (int i = 0; i < allCounts.Count && i < 20; i++)
        {
            var (pdf, count) = allCounts[i];

            if (count == 0)
                continue;

            table.AddRow(
                (i + 1).ToString(),
                ConsoleRenderingService.SafeMarkup(pdfDescriptionService.GetItemDescription(pdf)),
                count.ToString()
            );
        }

        AnsiConsole.Write(table);

        if (allCounts.Count > 20 && allCounts[20].Count > 0)
        {
            AnsiConsole.MarkupLine(
                $"[grey]... and {allCounts.Count(c => c.Count > 0) - 20} more PDFs with occurrences[/]"
            );
        }
    }

    /// <summary>
    /// Handle the select command
    /// </summary>
    private bool HandleSelectCommand(
        List<PdfData> allPdfData,
        string[] parts,
        CancellationToken cancellationToken
    )
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out int pdfIndex))
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] select <number>");
            return false;
        }

        // Adjust for 1-based indexing that users see
        pdfIndex--;

        if (pdfIndex < 0 || pdfIndex >= allPdfData.Count)
        {
            AnsiConsole.MarkupLine(
                $"[red]Invalid PDF number.[/] Valid range: 1-{allPdfData.Count}"
            );
            return false;
        }

        var selectedPdf = allPdfData[pdfIndex];
        string safeTitle = ConsoleRenderingService.SafeMarkup(
            pdfDescriptionService.GetItemDescription(selectedPdf)
        );

        AnsiConsole.MarkupLine($"Selected [yellow]{safeTitle}[/]");
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .AddChoices(
                    new[] { "Work with this PDF in detail", "Cancel and return to all PDFs mode" }
                )
        );

        if (choice == "Work with this PDF in detail")
        {
            RunSinglePdfRepl(selectedPdf, cancellationToken);

            // Ask if the user wants to return to all PDFs mode or exit
            return AnsiConsole.Confirm("Return to all PDFs mode?");
        }

        return false;
    }

    /// <summary>
    /// Run non-interactive search across all PDFs and optionally export results
    /// </summary>
    /// <param name="pattern">Search pattern (regex supported)</param>
    /// <param name="exportPath">Optional path to export results</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of results found</returns>
    public int RunNonInteractiveSearch(
        string pattern,
        string exportPath,
        CancellationToken cancellationToken = default
    )
    {
        var pdfDataList = dataLoadingService.LoadPdfDataFromDirectory(
            _pathsOptions.PdfDataDir,
            _pathsOptions.PaperMetadataDir
        );

        if (pdfDataList.Count == 0)
        {
            logger.LogWarning(
                "No PDF data could be loaded. Please run the analyze pdfs command first."
            );
            return 0;
        }

        logger.LogInformation(
            "Searching for pattern '{Pattern}' across {Count} PDFs...",
            pattern,
            pdfDataList.Count
        );

        try
        {
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            int totalMatches = 0;

            var allResults = new List<dynamic>();

            foreach (var pdf in pdfDataList)
            {
                try
                {
                    // Skip invalid PDFs
                    if (pdf == null || pdf.Texts == null)
                        continue;

                    var results = new List<(int PageIdx, string Text, string Match)>();

                    // Search in full text
                    for (int i = 0; i < pdf.Texts.Length; i++)
                    {
                        var text = pdf.Texts[i];
                        if (string.IsNullOrEmpty(text))
                            continue;

                        var matches = regex.Matches(text);
                        foreach (Match match in matches)
                        {
                            // Get some context around the match
                            int start = Math.Max(0, match.Index - 40);
                            int length = Math.Min(text.Length - start, match.Length + 80);
                            string context = text.Substring(start, length);

                            results.Add((i, context, match.Value));
                        }
                    }

                    if (results.Count > 0)
                    {
                        // Add this PDF's results to the collection
                        allResults.Add(
                            new
                            {
                                PDF = pdfDescriptionService.GetItemDescription(pdf),
                                FileName = pdf.FileName,
                                ResultCount = results.Count,
                                Results = results
                                    .Select(r => new
                                    {
                                        Page = r.PageIdx + 1,
                                        Context = r.Text,
                                        Match = r.Match,
                                    })
                                    .ToList(),
                            }
                        );

                        totalMatches += results.Count;

                        logger.LogInformation(
                            "Found {Count} matches in {Pdf}",
                            results.Count,
                            pdfDescriptionService.GetItemDescription(pdf)
                        );
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        "Error searching PDF {FileName}: {Error}",
                        pdf?.FileName ?? "unknown",
                        ex.Message
                    );
                }
            }

            // Create export data
            var exportData = new
            {
                Pattern = pattern,
                TotalMatches = totalMatches,
                PDFCount = pdfDataList.Count,
                PDFsWithMatches = allResults.Count,
                Timestamp = DateTime.Now,
                Results = allResults,
            };

            // Export if path is provided or log the results
            if (!string.IsNullOrEmpty(exportPath))
            {
                if (jsonExportService.ExportToJson(exportData, exportPath))
                {
                    logger.LogInformation(
                        "Exported {Count} matches to {Path}",
                        totalMatches,
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
                if (totalMatches > 0)
                {
                    logger.LogInformation(
                        "Found {Count} matches in {PdfCount} PDFs. No export path provided.",
                        totalMatches,
                        allResults.Count
                    );
                }
                else
                {
                    logger.LogInformation("No matches found for pattern '{Pattern}'", pattern);
                }
            }

            return totalMatches;
        }
        catch (RegexParseException ex)
        {
            logger.LogError("Invalid regex pattern: {Error}", ex.Message);
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during non-interactive search: {Error}", ex.Message);
            return 0;
        }
    }

    /// <summary>
    /// Run non-interactive evaluation of an expression across all PDFs
    /// </summary>
    /// <param name="expression">Keyword expression to evaluate</param>
    /// <param name="exportPath">Optional path to export results</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of PDFs matching the expression</returns>
    public int RunNonInteractiveEvaluation(
        string expression,
        string exportPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var pdfDataList = dataLoadingService.LoadPdfDataFromDirectory(
            _pathsOptions.PdfDataDir,
            _pathsOptions.PaperMetadataDir
        );

        if (pdfDataList.Count == 0)
        {
            logger.LogWarning(
                "No PDF data could be loaded. Please run the analyze pdfs command first."
            );
            return 0;
        }

        logger.LogInformation(
            "Evaluating expression '{Expression}' across {Count} PDFs...",
            expression,
            pdfDataList.Count
        );

        try
        {
            // Get all keywords used in the expression
            var keywords = _keywordsOptions.Analysis;

            // Parse the expression
            var expressionFunc = KeywordExpressionParser.ParseExpression(expression);

            // Filter PDFs
            var matchingPdfs = new List<(PdfData Pdf, Dictionary<string, int> Counts)>();

            foreach (var pdf in pdfDataList)
            {
                var counts = CountKeywords(pdf, keywords);
                if (expressionFunc(counts))
                {
                    matchingPdfs.Add((pdf, counts));
                }
            }

            // Create export data
            var exportData = new
            {
                Expression = expression,
                TotalMatches = matchingPdfs.Count,
                Timestamp = DateTime.Now,
                MatchingPDFs = matchingPdfs
                    .Select(p => new
                    {
                        PDF = pdfDescriptionService.GetItemDescription(p.Pdf),
                        FileName = p.Pdf.FileName,
                        KeywordCounts = p.Counts,
                    })
                    .ToList(),
            };

            // Export if path is provided or log the results
            if (!string.IsNullOrEmpty(exportPath))
            {
                if (jsonExportService.ExportToJson(exportData, exportPath))
                {
                    logger.LogInformation(
                        "Exported {Count} matching PDFs to {Path}",
                        matchingPdfs.Count,
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
                if (matchingPdfs.Count > 0)
                {
                    logger.LogInformation(
                        "Found {Count} PDFs matching expression '{Expression}'. No export path provided.",
                        matchingPdfs.Count,
                        expression
                    );

                    // Log a sample of the results
                    foreach (var pdf in matchingPdfs.Take(5))
                    {
                        logger.LogInformation(
                            "Matching PDF: {PDF} (keywords: {Keywords})",
                            pdfDescriptionService.GetItemDescription(pdf.Pdf),
                            string.Join(
                                ", ",
                                pdf.Counts.Where(c => c.Value > 0)
                                    .Select(c => $"{c.Key}: {c.Value}")
                            )
                        );
                    }

                    if (matchingPdfs.Count > 5)
                    {
                        logger.LogInformation(
                            "... and {Count} more matching PDFs",
                            matchingPdfs.Count - 5
                        );
                    }
                }
                else
                {
                    logger.LogInformation(
                        "No PDFs matched the expression '{Expression}'",
                        expression
                    );
                }
            }

            return matchingPdfs.Count;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during non-interactive evaluation: {Error}", ex.Message);
            return 0;
        }
    }
}
