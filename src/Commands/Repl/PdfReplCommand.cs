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
/// PDF REPL command for testing keyword expressions against PDF content
/// </summary>
public class PdfReplCommand(
    ILogger<PdfReplCommand> logger,
    IOptions<PathsOptions> pathsOptions,
    PdfDescriptionService pdfDescriptionService,
    ConsoleRenderingService renderingService,
    DataLoadingService dataLoadingService
) : BaseReplCommand(logger)
{
    private readonly PathsOptions _pathsOptions = pathsOptions.Value;

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
                .AddChoices(["Work with a single PDF", "Work with all PDFs"])
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
            { "eval <expression>", "Evaluate a keyword expression" },
            { "search <pattern>", "Search for a pattern (case-insensitive regex)" },
            { "showall [true|false]", "Toggle showing all results (no result limits)" },
            { "export [filepath]", "Export the last search results to JSON" },
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
                    case "eval":
                        HandleEvalCommand(pdfData, input.Substring(5));
                        break;
                    case "search":
                        HandleSearchCommand(pdfData, parts);
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
            { "export [filepath]", "Export the last search results to JSON" },
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
                HandleError(ex, "all PDFs REPL");
            }
        }
    }

    /// <summary>
    /// Count occurrences of a keyword in a PDF
    /// </summary>
    private static Dictionary<string, int> CountKeywords(PdfData pdfData, params string[] keywords)
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
    /// Handle the eval command
    /// </summary>
    private void HandleEvalCommand(PdfData pdfData, string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] eval <expression>");
            return;
        }

        AnsiConsole.MarkupLine(
            $"Evaluating expression: [yellow]{ConsoleRenderingService.SafeMarkup(expression)}[/]"
        );

        try
        {
            // Extract keywords from the expression
            var keywords = ExtractKeywordsFromExpression(expression);
            if (keywords.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]No keywords found in expression[/]");
                return;
            }

            // Count occurrences of keywords
            var counts = CountKeywords(pdfData, keywords.ToArray());

            // Parse the expression
            var expressionFunc = KeywordExpressionParser.ParseExpression(expression);

            // Evaluate the expression
            bool result = expressionFunc(counts);

            // Create and store the result for potential export
            var evalResult = new PdfEvaluationResult
            {
                Expression = expression,
                TotalMatches = result ? 1 : 0,
                Timestamp = DateTime.Now,
                MatchingPdfs = result
                    ? new List<PdfEvaluationItem>
                    {
                        new PdfEvaluationItem
                        {
                            PdfName = pdfDescriptionService.GetItemDescription(pdfData),
                            Filename = pdfData.FileName,
                            KeywordCounts = counts,
                        },
                    }
                    : new List<PdfEvaluationItem>(),
            };

            LastSearchResults = evalResult;

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
    /// Handle the search command
    /// </summary>
    private void HandleSearchCommand(PdfData pdfData, string[] parts)
    {
        if (parts.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] search <pattern>");
            return;
        }

        string pattern = string.Join(" ", parts.Skip(1));

        AnsiConsole.MarkupLine(
            $"Searching for: [yellow]{ConsoleRenderingService.SafeMarkup(pattern)}[/]"
        );

        try
        {
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            var results = new List<(int PageNum, int LineNum, MatchObject Line)>();

            // Initialize a search result to store
            var pdfSearchResult = new PdfSearchResult
            {
                Pattern = pattern,
                TotalMatches = 0,
                Timestamp = DateTime.Now,
                Results = new List<PdfSearchItem>(),
            };

            var pdfSearchItem = new PdfSearchItem
            {
                PdfName = pdfDescriptionService.GetItemDescription(pdfData),
                Filename = pdfData.FileName,
                MatchCount = 0,
                Context = new List<PdfMatchContext>(),
            };

            // Search in lines
            for (int i = 0; i < pdfData.TextLines.Length; i++)
            {
                var pageLines = pdfData.TextLines[i];
                if (pageLines == null)
                    continue;

                for (int j = 0; j < pageLines.Length; j++)
                {
                    var line = pageLines[j];
                    if (line == null || string.IsNullOrEmpty(line.Text))
                        continue;

                    if (regex.IsMatch(line.Text))
                    {
                        results.Add((PageNum: i, LineNum: j, Line: line));

                        // Add to the search result
                        pdfSearchItem.Context.Add(
                            new PdfMatchContext
                            {
                                Page = i + 1,
                                Match = regex.Match(line.Text).Value,
                                Context = line.Text,
                            }
                        );
                    }
                }
            }

            pdfSearchItem.MatchCount = results.Count;
            pdfSearchResult.TotalMatches = results.Count;
            pdfSearchResult.Results.Add(pdfSearchItem);

            // Store for potential export
            LastSearchResults = pdfSearchResult;

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
                string context = ConsoleRenderingService.SafeMarkup(result.Line.Text);
                table.AddRow((result.PageNum + 1).ToString(), context);
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

        AnsiConsole.MarkupLine(
            $"Filtering PDFs with expression: [yellow]{ConsoleRenderingService.SafeMarkup(expression)}[/]"
        );

        try
        {
            // Extract keywords from the expression
            var keywords = ExtractKeywordsFromExpression(expression);
            if (keywords.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]No keywords found in expression[/]");
                return;
            }

            // Parse the expression
            var expressionFunc = KeywordExpressionParser.ParseExpression(expression);

            // Filter PDFs
            var matchingPdfs = new List<(PdfData Pdf, Dictionary<string, int> Counts)>();

            foreach (var pdf in allPdfData)
            {
                var counts = CountKeywords(pdf, keywords.ToArray());
                if (expressionFunc(counts))
                {
                    matchingPdfs.Add((pdf, counts));
                }
            }

            // Create the evaluation result for export
            var evalResult = new PdfEvaluationResult
            {
                Expression = expression,
                TotalMatches = matchingPdfs.Count,
                Timestamp = DateTime.Now,
                MatchingPdfs = matchingPdfs
                    .Select(p => new PdfEvaluationItem
                    {
                        PdfName = pdfDescriptionService.GetItemDescription(p.Pdf),
                        Filename = p.Pdf.FileName,
                        KeywordCounts = p.Counts,
                    })
                    .ToList(),
            };

            LastSearchResults = evalResult;

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
                .AddChoices(["Work with this PDF in detail", "Cancel and return to all PDFs mode"])
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
        string? exportPath,
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
            "Searching for pattern '{Pattern}' in {Count} PDFs...",
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
            var exportData = new PdfSearchResult
            {
                Pattern = pattern,
                TotalMatches = totalMatches,
                Timestamp = DateTime.Now,
                Results = allResults
                    .Select(result => new PdfSearchItem
                    {
                        PdfName = (string)result.PDF,
                        Filename = (string)result.FileName,
                        MatchCount = (int)result.ResultCount,
                        Context = ((System.Collections.IEnumerable)result.Results)
                            .Cast<dynamic>()
                            .Select(r => new PdfMatchContext
                            {
                                Page = (int)r.Page,
                                Match = (string)r.Match,
                                Context = (string)r.Context,
                            })
                            .ToList(),
                    })
                    .ToList(),
            };

            // Export if path is provided or log the results
            if (!string.IsNullOrEmpty(exportPath))
            {
                if (WriteToFile(exportData, exportPath, ReplJsonContext.Default.PdfSearchResult))
                {
                    logger.LogInformation(
                        "Exported {Count} results to {Path}",
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
                        "Found {Count} matches in PDF files. No export path provided.",
                        totalMatches
                    );

                    // Log a sample of the results
                    foreach (var pdf in allResults.Take(3))
                    {
                        logger.LogInformation(
                            "Found {Count} matches in {Pdf}",
                            (int)pdf.Results.Count,
                            (string)pdf.PDF
                        );
                    }

                    if (allResults.Count > 3)
                    {
                        logger.LogInformation(
                            "... and {Count} more PDFs with matches",
                            allResults.Count - 3
                        );
                    }
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
        string? exportPath = null,
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
            // Extract keywords from the expression
            var keywords = ExtractKeywordsFromExpression(expression);
            if (keywords.Count == 0)
            {
                logger.LogWarning("No keywords found in expression");
                return 0;
            }

            // Parse the expression
            var expressionFunc = KeywordExpressionParser.ParseExpression(expression);

            // Filter PDFs
            var matchingPdfs = new List<(PdfData Pdf, Dictionary<string, int> Counts)>();

            foreach (var pdf in pdfDataList)
            {
                var counts = CountKeywords(pdf, keywords.ToArray());
                if (expressionFunc(counts))
                {
                    matchingPdfs.Add((pdf, counts));
                }
            }

            // Create export data
            var exportData = new PdfEvaluationResult
            {
                Expression = expression,
                TotalMatches = matchingPdfs.Count,
                Timestamp = DateTime.Now,
                MatchingPdfs = matchingPdfs
                    .Select(p => new PdfEvaluationItem
                    {
                        PdfName = pdfDescriptionService.GetItemDescription(p.Pdf),
                        Filename = p.Pdf.FileName,
                        KeywordCounts = p.Counts,
                    })
                    .ToList(),
            };

            // Export if path is provided or log the results
            if (!string.IsNullOrEmpty(exportPath))
            {
                if (
                    WriteToFile(exportData, exportPath, ReplJsonContext.Default.PdfEvaluationResult)
                )
                {
                    logger.LogInformation(
                        "Exported {Count} matching PDFs to {Path}",
                        matchingPdfs.Count,
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
                logger.LogInformation("{Count} PDFs matched the expression", matchingPdfs.Count);
            }

            return matchingPdfs.Count;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error evaluating expression: {Message}", ex.Message);
            return 0;
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
        foreach (var part in cleaned.Split([' '], StringSplitOptions.RemoveEmptyEntries))
        {
            // If not a number, it might be a keyword
            if (!int.TryParse(part, out _))
            {
                keywords.Add(part.Trim());
            }
        }

        return keywords;
    }

    /// <summary>
    /// Override the HandleExportCommand method to use source generation for exporting
    /// </summary>
    protected override bool HandleExportCommand(string[] parts, object? data = null)
    {
        string? filePath = null;
        if (parts.Length > 1)
        {
            filePath = parts[1];
        }

        object? exportData = data ?? LastSearchResults;
        if (exportData == null)
        {
            AnsiConsole.MarkupLine("[red]No data available to export[/]");
            return false;
        }

        try
        {
            // Create default file path if none was provided
            if (string.IsNullOrEmpty(filePath))
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                filePath = System.IO.Path.Combine(
                    System.IO.Directory.GetCurrentDirectory(),
                    $"export-{timestamp}.json"
                );
            }

            bool success = false;

            // Use appropriate serializer based on the type
            if (exportData is PdfSearchResult searchResult)
            {
                success = WriteToFile(
                    searchResult,
                    filePath,
                    ReplJsonContext.Default.PdfSearchResult
                );
            }
            else if (exportData is PdfEvaluationResult evalResult)
            {
                success = WriteToFile(
                    evalResult,
                    filePath,
                    ReplJsonContext.Default.PdfEvaluationResult
                );
            }
            else
            {
                AnsiConsole.MarkupLine(
                    $"[red]Invalid data type for export:[/] {exportData.GetType()}"
                );
                return false;
            }

            if (success)
            {
                AnsiConsole.MarkupLine(
                    $"[green]Data exported to:[/] {ConsoleRenderingService.SafeMarkup(LastExportedFilePath)}"
                );
                return true;
            }
            else
            {
                AnsiConsole.MarkupLine("[red]Failed to export data[/]");
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
}
