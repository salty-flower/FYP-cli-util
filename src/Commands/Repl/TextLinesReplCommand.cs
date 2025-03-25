using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using DataCollection.Models;
using DataCollection.Models.Export;
using DataCollection.Options;
using DataCollection.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace DataCollection.Commands.Repl;

/// <summary>
/// TextLines REPL command for inspecting PDF text lines
/// </summary>
public class TextLinesReplCommand(
    ILogger<TextLinesReplCommand> logger,
    IOptions<PathsOptions> pathsOptions,
    PdfDescriptionService pdfDescriptionService,
    ConsoleRenderingService renderingService,
    PdfSearchService searchService,
    DataLoadingService dataLoadingService
) : BaseReplCommand(logger)
{
    private readonly PathsOptions _pathsOptions = pathsOptions.Value;

    /// <summary>
    /// Run the TextLines REPL
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

        // Ask user if they want to inspect a single PDF or work with all PDFs
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("How would you like to work with the [green]PDFs[/]?")
                .PageSize(10)
                .AddChoices(new[] { "Inspect a single PDF", "Work with all PDFs" })
        );

        if (choice == "Inspect a single PDF")
        {
            // Select a single PDF to inspect
            var pdfChoices = pdfDataList
                .Select(pdf =>
                    ConsoleRenderingService.SafeMarkup(
                        pdfDescriptionService.GetItemDescription(pdf)
                    )
                )
                .ToArray();

            var selectedPdfDesc = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select a [green]PDF[/] to inspect")
                    .PageSize(20)
                    .AddChoices(pdfChoices)
            );

            var selectedPdf = pdfDataList[Array.IndexOf(pdfChoices, selectedPdfDesc)];
            RunSinglePdfRepl(selectedPdf, pdfDataList, cancellationToken);
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
    private void RunSinglePdfRepl(
        PdfData pdfData,
        List<PdfData> allPdfData,
        CancellationToken cancellationToken
    )
    {
        AnsiConsole.Clear();
        string safeTitle = ConsoleRenderingService.SafeMarkup(
            pdfDescriptionService.GetItemDescription(pdfData)
        );
        AnsiConsole.Write(
            new Rule($"TextLines REPL for [yellow]{safeTitle}[/]").RuleStyle("green")
        );
        AnsiConsole.MarkupLine($"Document has [blue]{pdfData.TextLines.Length}[/] pages");

        var commands = new Dictionary<string, string>
        {
            { "page <number> [line]", "View all lines on a page or a specific line" },
            {
                "search <pattern> [page]",
                "Search text using regex pattern (optional: on specific page)"
            },
            { "searchall <pattern>", "Search text across all loaded PDFs" },
            { "showall [true|false]", "Toggle showing all results (no result limits)" },
            { "export [filename]", "Export last search results to JSON" },
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
                    case "page":
                        HandlePageCommand(pdfData, parts);
                        break;
                    case "search":
                        HandleSearchCommand(pdfData, parts);
                        break;
                    case "searchall":
                        HandleSearchAllCommand(allPdfData, parts);
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
            new Rule(
                $"TextLines REPL for [yellow]ALL PDFs[/] ({allPdfData.Count} documents)"
            ).RuleStyle("green")
        );

        var commands = new Dictionary<string, string>
        {
            { "search <pattern>", "Search all PDFs for a pattern" },
            { "list", "List all available PDFs" },
            { "select <number>", "Select a specific PDF to inspect" },
            { "showall [true|false]", "Toggle showing all results (no result limits)" },
            { "export [filename]", "Export last search results to JSON" },
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
                    case "search":
                        HandleSearchAllCommand(allPdfData, parts);
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
    /// Handle the page command
    /// </summary>
    private void HandlePageCommand(PdfData pdfData, string[] parts)
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out int pageNum))
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] page <number> [line]");
            return;
        }

        // Adjust for 0-based indexing
        pageNum--;

        if (pageNum < 0 || pageNum >= pdfData.TextLines.Length)
        {
            AnsiConsole.MarkupLine(
                $"[red]Invalid page number.[/] Valid range: 1-{pdfData.TextLines.Length}"
            );
            return;
        }

        // If a specific line is requested
        if (parts.Length > 2 && int.TryParse(parts[2], out int lineNum))
        {
            // Adjust for 0-based indexing
            lineNum--;

            var lines = pdfData.TextLines[pageNum];
            if (lineNum < 0 || lineNum >= lines.Length)
            {
                AnsiConsole.MarkupLine(
                    $"[red]Invalid line number.[/] Valid range: 1-{lines.Length}"
                );
                return;
            }

            ConsoleRenderingService.DisplayTextLine(pageNum, lineNum, lines[lineNum]);
        }
        else
        {
            // Display all lines on the page
            ConsoleRenderingService.DisplayPage(pdfData, pageNum);
        }
    }

    /// <summary>
    /// Handle the search command for a single PDF
    /// </summary>
    private void HandleSearchCommand(PdfData pdfData, string[] parts)
    {
        if (parts.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] search <pattern> [page]");
            return;
        }

        try
        {
            string pattern = PdfSearchService.ParseSearchPattern(parts);
            int? pageNumber = null;

            if (parts.Length > 2 && int.TryParse(parts[2], out int page))
            {
                pageNumber = page - 1; // Convert to 0-based index
            }

            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            var results = searchService.SearchInPdf(pdfData, regex, pageNumber);

            string title = pageNumber.HasValue
                ? $"Page {pageNumber.Value + 1} in {pdfDescriptionService.GetItemDescription(pdfData)}"
                : pdfDescriptionService.GetItemDescription(pdfData);

            ConsoleRenderingService.DisplaySearchResults(
                results,
                pattern,
                ConsoleRenderingService.SafeMarkup(title),
                maxToShow: 50,
                showAll: ShowAllResults
            );

            // Store results for potential export
            LastSearchResults = new
            {
                Pattern = pattern,
                PDF = pdfDescriptionService.GetItemDescription(pdfData),
                PageNumber = pageNumber,
                Results = results.ConvertAll(r => new
                {
                    Page = r.PageNum + 1,
                    Line = r.LineNum + 1,
                    Text = r.Line?.Text,
                    Position = new
                    {
                        X0 = r.Line?.X0,
                        Top = r.Line?.Top,
                        X1 = r.Line?.X1,
                        Bottom = r.Line?.Bottom,
                    },
                }),
            };
        }
        catch (RegexParseException ex)
        {
            AnsiConsole.MarkupLine(
                $"[red]Invalid regex pattern:[/] {ConsoleRenderingService.SafeMarkup(ex.Message)}"
            );
        }
    }

    /// <summary>
    /// Handle the search command for all PDFs
    /// </summary>
    private void HandleSearchAllCommand(List<PdfData> allPdfData, string[] parts)
    {
        if (parts.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] searchall <pattern>");
            return;
        }

        try
        {
            string pattern = PdfSearchService.ParseSearchPattern(parts);
            AnsiConsole.MarkupLine(
                $"Searching for '{ConsoleRenderingService.SafeMarkup(pattern)}' across all PDFs..."
            );

            var regex = new Regex(pattern, RegexOptions.IgnoreCase);

            int total = 0;
            int pdfsWithMatches = 0;
            var allResults = new List<dynamic>();

            foreach (var pdf in allPdfData)
            {
                try
                {
                    // Skip null PDFs or PDFs with null TextLines
                    if (pdf == null || pdf.TextLines == null)
                    {
                        continue;
                    }

                    var results = searchService.SearchInPdf(pdf, regex);

                    // Skip if no results or all results are null
                    if (results == null || results.Count == 0 || results.All(r => r.Line == null))
                    {
                        continue;
                    }

                    string safeTitle = ConsoleRenderingService.SafeMarkup(
                        pdfDescriptionService.GetItemDescription(pdf)
                    );
                    AnsiConsole.MarkupLine($"[green]Results in:[/] {safeTitle}");

                    ConsoleRenderingService.DisplaySearchResults(
                        results,
                        pattern,
                        safeTitle,
                        maxToShow: 10,
                        showAll: ShowAllResults
                    );

                    // Add to all results - ensure we handle potential null values
                    var validResults = results
                        .Where(r => r.Line != null)
                        .Select(r => new
                        {
                            Page = r.PageNum + 1,
                            Line = r.LineNum + 1,
                            Text = r.Line?.Text ?? "<null text>",
                            Position = r.Line == null
                                ? null
                                : new
                                {
                                    X0 = r.Line.X0,
                                    Top = r.Line.Top,
                                    X1 = r.Line.X1,
                                    Bottom = r.Line.Bottom,
                                },
                        })
                        .ToList();

                    if (validResults.Any())
                    {
                        allResults.Add(
                            new
                            {
                                PDF = pdfDescriptionService.GetItemDescription(pdf),
                                FileName = pdf.FileName,
                                ResultCount = validResults.Count,
                                Results = validResults,
                            }
                        );

                        total += validResults.Count;
                        pdfsWithMatches++;
                    }

                    // Draw a separator between PDFs
                    AnsiConsole.WriteLine();
                }
                catch (Exception ex)
                {
                    // Log error but continue with other PDFs
                    logger.LogWarning(
                        "Error searching PDF {FileName}: {Error}",
                        pdf?.FileName ?? "unknown",
                        ex.Message
                    );
                }
            }

            // Store results for potential export
            LastSearchResults = new
            {
                Pattern = pattern,
                TotalMatches = total,
                PDFsWithMatches = pdfsWithMatches,
                Timestamp = DateTime.Now,
                Results = allResults,
            };

            if (pdfsWithMatches > 0)
            {
                AnsiConsole.MarkupLine($"[blue]Found {total} matches in {pdfsWithMatches} PDFs[/]");
            }
            else
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]No matches found[/] for '{ConsoleRenderingService.SafeMarkup(pattern)}'"
                );
            }
        }
        catch (RegexParseException ex)
        {
            AnsiConsole.MarkupLine(
                $"[red]Invalid regex pattern:[/] {ConsoleRenderingService.SafeMarkup(ex.Message)}"
            );
        }
        catch (Exception ex)
        {
            // Catch and display any other exceptions
            HandleError(ex, "search all PDFs");
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
                    new[] { "Inspect this PDF in detail", "Cancel and return to all PDFs mode" }
                )
        );

        if (choice == "Inspect this PDF in detail")
        {
            RunSinglePdfRepl(selectedPdf, allPdfData, cancellationToken);

            // Ask if the user wants to return to all PDFs mode or exit
            return AnsiConsole.Confirm("Return to all PDFs mode?");
        }

        return false;
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
            if (LastSearchResults is TextLinesSearchResult searchResult)
            {
                return WriteToFile(
                    searchResult,
                    filename,
                    ReplJsonContext.Default.TextLinesSearchResult
                );
            }
            else
            {
                AnsiConsole.MarkupLine(
                    "[red]Unknown data type for export:[/] {Type}",
                    LastSearchResults?.GetType().Name ?? "null"
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
    /// Run non-interactive search across all PDFs and optionally export results
    /// </summary>
    /// <param name="pattern">Search pattern (regex supported)</param>
    /// <param name="results">Out parameter to store the search results</param>
    /// <param name="exportPath">Optional path to export results</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of results found</returns>
    public int RunNonInteractiveSearch(
        string pattern,
        out TextLinesSearchResult results,
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
            results = null;
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
            int total = 0;
            int pdfsWithMatches = 0;
            var pdfResults = new List<TextLinesPdfResult>();

            foreach (var pdf in pdfDataList)
            {
                try
                {
                    if (pdf == null || pdf.TextLines == null)
                        continue;

                    var searchResults = searchService.SearchInPdf(pdf, regex);

                    if (
                        searchResults == null
                        || searchResults.Count == 0
                        || searchResults.All(r => r.Line == null)
                    )
                        continue;

                    // Add to all results - ensure we handle potential null values
                    var textLinesResults = searchResults
                        .Where(r => r.Line != null)
                        .Select(r => new TextLinesResult { Text = r.Line?.Text ?? "<null text>" })
                        .ToList();

                    if (textLinesResults.Any())
                    {
                        pdfResults.Add(
                            new TextLinesPdfResult
                            {
                                Pdf = pdfDescriptionService.GetItemDescription(pdf),
                                FileName = pdf.FileName,
                                ResultCount = textLinesResults.Count,
                                Results = textLinesResults,
                            }
                        );

                        total += textLinesResults.Count;
                        pdfsWithMatches++;

                        logger.LogInformation(
                            "Found {Count} matches in {Pdf}",
                            textLinesResults.Count,
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

            // Create export data using source generation models
            results = new TextLinesSearchResult
            {
                Pattern = pattern,
                TotalMatches = total,
                PdfsWithMatches = pdfsWithMatches,
                Timestamp = DateTime.Now,
                Results = pdfResults,
            };

            // Store for later export if needed
            LastSearchResults = results;

            // Export if path is provided or log the results
            if (!string.IsNullOrEmpty(exportPath))
            {
                if (WriteToFile(results, exportPath, ReplJsonContext.Default.TextLinesSearchResult))
                {
                    logger.LogInformation("Exported {Count} results to {Path}", total, exportPath);
                }
                else
                {
                    logger.LogError("Failed to export results to {Path}", exportPath);
                }
            }
            else
            {
                logger.LogInformation(
                    "Found total of {Count} matches in {PdfCount} PDFs. No export path provided.",
                    total,
                    pdfsWithMatches
                );
            }

            return total;
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
}
