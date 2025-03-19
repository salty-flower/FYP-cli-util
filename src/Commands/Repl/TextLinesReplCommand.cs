using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DataCollection.Models;
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
            { "search <pattern>", "Search text across all PDFs" },
            { "list", "List all available PDFs" },
            { "select <number>", "Select a specific PDF to inspect in detail" },
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

            renderingService.DisplayTextLine(pageNum, lineNum, lines[lineNum]);
        }
        else
        {
            // Display all lines on the page
            renderingService.DisplayPage(pdfData, pageNum);
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

        // Get the pattern
        string pattern = searchService.ParseSearchPattern(parts);

        // Check if a specific page is requested
        int? pageNum = null;
        if (parts.Length > 2 && int.TryParse(parts[parts.Length - 1], out int page))
        {
            pageNum = page - 1; // Adjust for 0-based indexing
            if (pageNum < 0 || pageNum >= pdfData.TextLines.Length)
            {
                AnsiConsole.MarkupLine(
                    $"[red]Invalid page number.[/] Valid range: 1-{pdfData.TextLines.Length}"
                );
                return;
            }
        }

        try
        {
            var regex = new System.Text.RegularExpressions.Regex(
                pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            var results = searchService.SearchInPdf(pdfData, regex, pageNum);

            // Display results
            string pdfDesc = ConsoleRenderingService.SafeMarkup(
                pdfDescriptionService.GetItemDescription(pdfData)
            );
            renderingService.DisplaySearchResults(results, pattern, pdfDesc, pageNum);
        }
        catch (System.Text.RegularExpressions.RegexParseException ex)
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
            AnsiConsole.MarkupLine("[red]Usage:[/] search <pattern>");
            return;
        }

        // Get the pattern
        string pattern = searchService.ParseSearchPattern(parts);

        try
        {
            var regex = new System.Text.RegularExpressions.Regex(
                pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            AnsiConsole.Write(
                new Rule(
                    $"Searching for '{ConsoleRenderingService.SafeMarkup(pattern)}' across {allPdfData.Count} PDFs..."
                ).RuleStyle("green")
            );

            // Search across all PDFs
            const int maxPdfsToShow = 20; // Limit the number of PDFs to show
            var pdfResults = searchService.SearchAllPdfs(
                allPdfData,
                regex,
                PdfSearchService.DefaultMaxMatchesPerPdf,
                maxPdfsToShow
            );

            int totalMatches = 0;
            foreach (var pdfResult in pdfResults)
            {
                totalMatches += pdfResult.Value.Count;
            }

            // Display results
            if (totalMatches > 0)
            {
                AnsiConsole.Write(
                    new Rule($"Found {totalMatches} matches in {pdfResults.Count} PDFs").RuleStyle(
                        "green"
                    )
                );

                int pdfsShown = 0;

                foreach (var pdfResult in pdfResults)
                {
                    string pdfDesc = ConsoleRenderingService.SafeMarkup(
                        pdfDescriptionService.GetItemDescription(pdfResult.Key)
                    );

                    try
                    {
                        AnsiConsole.Write(
                            new Rule($"PDF: {pdfDesc} ({pdfResult.Value.Count} matches)").RuleStyle(
                                "blue"
                            )
                        );

                        renderingService.DisplaySearchResults(
                            pdfResult.Value,
                            pattern,
                            pdfDesc,
                            10
                        );
                        pdfsShown++;
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine(
                            $"[red]Error displaying results for PDF:[/] {ConsoleRenderingService.SafeMarkup(ex.Message)}"
                        );
                    }
                }

                if (pdfResults.Count > maxPdfsToShow)
                {
                    AnsiConsole.MarkupLine(
                        $"[yellow]Only showing {maxPdfsToShow} out of {pdfResults.Count} PDFs with matches.[/]"
                    );
                }
            }
            else
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]No matches found for '{ConsoleRenderingService.SafeMarkup(pattern)}' in any PDF[/]"
                );
            }
        }
        catch (System.Text.RegularExpressions.RegexParseException ex)
        {
            AnsiConsole.MarkupLine(
                $"[red]Invalid regex pattern:[/] {ConsoleRenderingService.SafeMarkup(ex.Message)}"
            );
        }
        catch (Exception ex)
        {
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
}
