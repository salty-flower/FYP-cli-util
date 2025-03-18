using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ConsoleAppFramework;
using DataCollection.Models;
using DataCollection.Options;
using DataCollection.Utils;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace DataCollection.Commands;

/// <summary>
/// Interactive REPL commands for analyzing papers
/// </summary>
[RegisterCommands("repl")]
public class ReplCommands(
    ILogger<ScrapeCommands> logger,
    IOptions<PathsOptions> pathsOptions,
    IOptions<KeywordsOptions> keywordsOptions
)
{
    private readonly PathsOptions _pathsOptions = pathsOptions.Value;
    private readonly KeywordsOptions _keywordsOptions = keywordsOptions.Value;
    private List<Paper> _paperCache = new();

    /// <summary>
    /// Interactive REPL for testing keyword expressions against paper abstract and title
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public void Metadata(CancellationToken cancellationToken = default)
    {
        var paperMetadataDir = new DirectoryInfo(_pathsOptions.PaperMetadataDir);

        logger.LogInformation("Loading papers from metadata...");
        var papers = LoadPapersFromMetadata(paperMetadataDir);

        if (papers.Count == 0)
        {
            logger.LogWarning("No papers found. Please run the scrape command first.");
            return;
        }

        // Pre-compute keyword counts for all papers
        var paperKeywordCounts = new Dictionary<Paper, Dictionary<string, int>>();
        foreach (var paper in papers)
        {
            paperKeywordCounts[paper] = PaperAnalyzer.CountKeywordsInText(
                $"{paper.Title} {paper.Abstract}",
                _keywordsOptions.Analysis
            );
        }

        RunExpressionRepl(papers, paperKeywordCounts, "metadata", cancellationToken);
    }

    /// <summary>
    /// Interactive REPL for testing keyword expressions against PDF content
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public void PDF(CancellationToken cancellationToken = default)
    {
        var pdfDataList = LoadPdfDataFromDirectory(_pathsOptions.PdfDataDir);

        if (pdfDataList.Count == 0)
        {
            logger.LogWarning(
                "No PDF data could be loaded. Please run the analyze pdfs command first."
            );
            return;
        }

        // Pre-compute keyword counts for all PDFs
        var pdfKeywordCounts = new Dictionary<PdfData, Dictionary<string, int>>();
        foreach (var pdfData in pdfDataList)
        {
            pdfKeywordCounts[pdfData] = PaperAnalyzer.CountKeywordsInTexts(
                pdfData.Texts,
                _keywordsOptions.Analysis
            );
        }

        RunExpressionRepl(pdfDataList, pdfKeywordCounts, "PDF content", cancellationToken);
    }

    /// <summary>
    /// Interactive REPL for inspecting PDF text lines
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public void TextLines(CancellationToken cancellationToken = default)
    {
        var pdfDataList = LoadPdfDataFromDirectory(_pathsOptions.PdfDataDir);

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
            var pdfChoices = pdfDataList.Select(pdf => SafeMarkup(GetItemDescription(pdf))).ToArray();
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

    // Helper method to load PDF data from a directory
    private List<PdfData> LoadPdfDataFromDirectory(string directoryPath)
    {
        var pdfDataDir = new DirectoryInfo(directoryPath);
        var pdfDataList = new List<PdfData>();

        if (!pdfDataDir.Exists || pdfDataDir.GetFiles("*.bin").Length == 0)
        {
            return pdfDataList;
        }

        logger.LogInformation("Loading PDF data...");

        foreach (var file in pdfDataDir.GetFiles("*.bin"))
        {
            try
            {
                var bin = File.ReadAllBytes(file.FullName);
                var pdfData = MemoryPackSerializer.Deserialize<PdfData>(bin);
                if (pdfData != null)
                {
                    pdfDataList.Add(pdfData);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    "Error loading PDF data {FileName}: {Error}",
                    file.Name,
                    SafeMarkup(ex.Message)
                );
            }
        }

        // Try to load paper metadata if not already loaded
        if (_paperCache.Count == 0)
        {
            var paperMetadataDir = new DirectoryInfo(_pathsOptions.PaperMetadataDir);
            if (paperMetadataDir.Exists)
            {
                logger.LogInformation("Loading paper metadata for enhanced PDF descriptions...");
                _paperCache = LoadPapersFromMetadata(paperMetadataDir);
            }
        }

        logger.LogInformation("Loaded {Count} PDF documents", pdfDataList.Count);
        return pdfDataList;
    }

    private void RunSinglePdfRepl(PdfData pdfData, List<PdfData> allPdfData, CancellationToken cancellationToken)
    {
        AnsiConsole.Clear();
        string safeTitle = SafeMarkup(GetItemDescription(pdfData));
        AnsiConsole.Write(new Rule($"TextLines REPL for [yellow]{safeTitle}[/]").RuleStyle("green"));
        AnsiConsole.MarkupLine($"Document has [blue]{pdfData.TextLines.Length}[/] pages");
        
        var table = new Table();
        table.AddColumn("Command");
        table.AddColumn("Description");
        table.AddRow("page <number> [line]", "View all lines on a page or a specific line");
        table.AddRow("search <pattern> [page]", "Search text using regex pattern (optional: on specific page)");
        table.AddRow("searchall <pattern>", "Search text across all loaded PDFs");
        table.AddRow("info", "Show document information");
        table.AddRow("exit", "Exit REPL");
        
        AnsiConsole.Write(table);

        while (!cancellationToken.IsCancellationRequested)
        {
            var input = AnsiConsole.Prompt(
                new TextPrompt<string>("> ")
                    .PromptStyle("green"));

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (input.Equals("exit", StringComparison.CurrentCultureIgnoreCase))
                break;

            if (input.Equals("info", StringComparison.CurrentCultureIgnoreCase))
            {
                DisplayDocumentInfo(pdfData);
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
                        AnsiConsole.MarkupLine($"[red]Unknown command:[/] {SafeMarkup(parts[0])}");
                        break;
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {SafeMarkup(ex.Message)}");
            }
        }
    }

    private void RunAllPdfsRepl(List<PdfData> allPdfData, CancellationToken cancellationToken)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(
            new Rule(
                $"TextLines REPL for [yellow]ALL PDFs[/] ({allPdfData.Count} documents)"
            ).RuleStyle("green")
        );

        var table = new Table();
        table.AddColumn("Command");
        table.AddColumn("Description");
        table.AddRow("search <pattern>", "Search text across all PDFs");
        table.AddRow("list", "List all available PDFs");
        table.AddRow("select <number>", "Select a specific PDF to inspect in detail");
        table.AddRow("info", "Show summary information about all PDFs");
        table.AddRow("exit", "Exit REPL");

        AnsiConsole.Write(table);

        while (!cancellationToken.IsCancellationRequested)
        {
            var input = AnsiConsole.Prompt(new TextPrompt<string>("> ").PromptStyle("green"));

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (input.Equals("exit", StringComparison.CurrentCultureIgnoreCase))
                break;

            if (input.Equals("list", StringComparison.CurrentCultureIgnoreCase))
            {
                ListAllPdfs(allPdfData);
                continue;
            }

            if (input.Equals("info", StringComparison.CurrentCultureIgnoreCase))
            {
                DisplayAllPdfsInfo(allPdfData);
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
                        AnsiConsole.MarkupLine($"[red]Unknown command:[/] {parts[0]}");
                        break;
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            }
        }
    }

    private void ListAllPdfs(List<PdfData> allPdfData)
    {
        var table = new Table();
        table.AddColumn("#");
        table.AddColumn("PDF Name");
        table.AddColumn("Pages");

        for (int i = 0; i < allPdfData.Count; i++)
        {
            var pdf = allPdfData[i];
            table.AddRow(
                (i + 1).ToString(),
                SafeMarkup(GetItemDescription(pdf)),
                pdf.TextLines.Length.ToString()
            );
        }

        AnsiConsole.Write(new Rule($"Available PDFs ({allPdfData.Count})").RuleStyle("blue"));
        AnsiConsole.Write(table);
    }

    private void DisplayAllPdfsInfo(List<PdfData> allPdfData)
    {
        AnsiConsole.Write(new Rule("PDF Collection Summary").RuleStyle("blue"));

        // Count total pages
        int totalPages = allPdfData.Sum(pdf => pdf.TextLines.Length);
        double avgPages = totalPages / (double)allPdfData.Count;

        var table = new Table();
        table.AddColumn("Metric");
        table.AddColumn("Value");

        table.AddRow("Total PDFs", allPdfData.Count.ToString());
        table.AddRow("Total Pages", totalPages.ToString());
        table.AddRow("Average Pages", avgPages.ToString("F1"));

        if (allPdfData.Count > 0)
        {
            var largest = allPdfData.OrderByDescending(pdf => pdf.TextLines.Length).First();
            var smallest = allPdfData.OrderBy(pdf => pdf.TextLines.Length).First();

            table.AddRow(
                "Largest Document",
                $"{SafeMarkup(GetItemDescription(largest))} ({largest.TextLines.Length} pages)"
            );
            table.AddRow(
                "Smallest Document",
                $"{SafeMarkup(GetItemDescription(smallest))} ({smallest.TextLines.Length} pages)"
            );
        }

        AnsiConsole.Write(table);
    }

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
        string safeTitle = SafeMarkup(GetItemDescription(selectedPdf));

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

    private void DisplayDocumentInfo(PdfData pdfData)
    {
        string safeTitle = SafeMarkup(GetItemDescription(pdfData));
        AnsiConsole.Write(new Rule($"Document Information: {safeTitle}").RuleStyle("blue"));

        var table = new Table();
        table.AddColumn("Page");
        table.AddColumn("Lines");

        // Display page statistics
        for (int i = 0; i < pdfData.TextLines.Length; i++)
        {
            table.AddRow((i + 1).ToString(), pdfData.TextLines[i].Length.ToString());
        }

        AnsiConsole.Write(table);
    }

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

        var lines = pdfData.TextLines[pageNum];

        // If a specific line is requested
        if (parts.Length > 2 && int.TryParse(parts[2], out int lineNum))
        {
            // Adjust for 0-based indexing
            lineNum--;

            if (lineNum < 0 || lineNum >= lines.Length)
            {
                AnsiConsole.MarkupLine(
                    $"[red]Invalid line number.[/] Valid range: 1-{lines.Length}"
                );
                return;
            }

            DisplayLine(pageNum, lineNum, lines[lineNum]);
        }
        else
        {
            // Display all lines on the page
            AnsiConsole.Write(
                new Rule($"Page {pageNum + 1} ({lines.Length} lines)").RuleStyle("blue")
            );

            var table = new Table();
            table.AddColumn("Line");
            table.AddColumn("Text");

            for (int i = 0; i < lines.Length; i++)
            {
                table.AddRow((i + 1).ToString(), SafeMarkup(lines[i].Text));
            }

            AnsiConsole.Write(table);
        }
    }

    private void HandleSearchCommand(PdfData pdfData, string[] parts)
    {
        if (parts.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] search <pattern> [page]");
            return;
        }

        // Get the pattern
        string pattern = ParseSearchPattern(parts);

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
            var results = new List<(int PageNum, int LineNum, MatchObject Line)>();

            // Search in specified page or all pages
            if (pageNum.HasValue)
            {
                SearchInPage(pdfData, regex, pageNum.Value, results);
            }
            else
            {
                for (int i = 0; i < pdfData.TextLines.Length; i++)
                {
                    SearchInPage(pdfData, regex, i, results);
                }
            }

            // Display results
            DisplaySearchResults(results, pattern, SafeMarkup(GetItemDescription(pdfData)), pageNum);
        }
        catch (System.Text.RegularExpressions.RegexParseException ex)
        {
            AnsiConsole.MarkupLine($"[red]Invalid regex pattern:[/] {SafeMarkup(ex.Message)}");
        }
    }

    private void HandleSearchAllCommand(List<PdfData> allPdfData, string[] parts)
    {
        if (parts.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] searchall <pattern>");
            return;
        }

        // Get the pattern
        string pattern = ParseSearchPattern(parts);

        try
        {
            var regex = new System.Text.RegularExpressions.Regex(
                pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            AnsiConsole.Write(
                new Rule($"Searching for '{Markup.Escape(pattern)}' across {allPdfData.Count} PDFs...").RuleStyle(
                    "green"
                )
            );

            // Track total matches
            int totalMatches = 0;
            int pdfWithMatches = 0;
            const int maxPdfsToShow = 20; // Limit the number of PDFs to show to prevent overwhelming output
            const int maxMatchesPerPdf = 500; // Limit matches per PDF

            // Results from each PDF
            var allResults =
                new Dictionary<string, List<(int PageNum, int LineNum, MatchObject Line)>>();

            // Process each PDF
            foreach (var pdf in allPdfData)
            {
                // Skip null PDFs
                if (pdf == null || pdf.TextLines == null)
                {
                    continue;
                }

                try
                {
                    var results = new List<(int PageNum, int LineNum, MatchObject Line)>();

                    for (int i = 0; i < pdf.TextLines.Length; i++)
                    {
                        SearchInPage(pdf, regex, i, results, maxMatchesPerPdf);
                        
                        // Stop if we've reached the limit
                        if (results.Count >= maxMatchesPerPdf)
                        {
                            break;
                        }
                    }

                    if (results.Count > 0)
                    {
                        string pdfDesc = SafeMarkup(GetItemDescription(pdf));
                        allResults[pdfDesc] = results;
                        totalMatches += results.Count;
                        pdfWithMatches++;
                    }
                }
                catch (Exception ex)
                {
                    // Log the error but continue with other PDFs
                    AnsiConsole.MarkupLine($"[red]Error searching PDF {SafeMarkup(pdf.FileName)}:[/] {SafeMarkup(ex.Message)}");
                }
            }

            // Display results
            if (totalMatches > 0)
            {
                AnsiConsole.Write(
                    new Rule($"Found {totalMatches} matches in {pdfWithMatches} PDFs").RuleStyle(
                        "green"
                    )
                );

                // Show a limited number of PDFs to prevent overwhelming output
                int pdfsToShow = Math.Min(maxPdfsToShow, allResults.Count);
                int pdfsShown = 0;

                foreach (var pdfResult in allResults)
                {
                    if (pdfsShown >= pdfsToShow)
                    {
                        AnsiConsole.MarkupLine($"[yellow]Only showing {pdfsToShow} out of {allResults.Count} PDFs with matches.[/]");
                        break;
                    }

                    try
                    {
                        // Note: pdfResult.Key is already escaped by SafeMarkup when added to the dictionary
                        AnsiConsole.Write(
                            new Rule(
                                $"PDF: {pdfResult.Key} ({pdfResult.Value.Count} matches)"
                            ).RuleStyle("blue")
                        );
                        DisplaySearchResults(pdfResult.Value, pattern, pdfResult.Key, 10);
                        pdfsShown++;
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Error displaying results for PDF:[/] {SafeMarkup(ex.Message)}");
                    }
                }
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]No matches found for '{Markup.Escape(pattern)}' in any PDF[/]");
            }
        }
        catch (System.Text.RegularExpressions.RegexParseException ex)
        {
            AnsiConsole.MarkupLine($"[red]Invalid regex pattern:[/] {SafeMarkup(ex.Message)}");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error during search:[/] {SafeMarkup(ex.Message)}");
        }
    }

    // Helper method to parse search patterns, handling quoted strings
    private string ParseSearchPattern(string[] parts)
    {
        string pattern = parts[1];
        if (pattern.StartsWith("\"") && parts.Length > 2)
        {
            // Reconstruct quoted search term
            var searchTermParts = new List<string> { parts[1] };
            int i = 2;
            while (i < parts.Length && !parts[i - 1].EndsWith("\""))
            {
                searchTermParts.Add(parts[i]);
                i++;
            }
            pattern = string.Join(" ", searchTermParts);

            // Remove quotes
            if (pattern.StartsWith("\"") && pattern.EndsWith("\""))
            {
                pattern = pattern.Substring(1, pattern.Length - 2);
            }
        }

        return pattern;
    }

    private void SearchInPage(
        PdfData pdfData,
        System.Text.RegularExpressions.Regex regex,
        int pageNum,
        List<(int, int, MatchObject)> results,
        int maxMatches = 1000 // Limit max matches to prevent overwhelming results
    )
    {
        // Check for valid page index
        if (pdfData == null || pdfData.TextLines == null || pageNum < 0 || pageNum >= pdfData.TextLines.Length)
        {
            return;
        }

        var lines = pdfData.TextLines[pageNum];
        
        // Check for null lines array
        if (lines == null)
        {
            return;
        }

        // Stop collecting if we've reached the maximum matches
        if (results.Count >= maxMatches)
        {
            return;
        }

        for (int j = 0; j < lines.Length; j++)
        {
            // Skip null lines
            if (lines[j] == null || string.IsNullOrEmpty(lines[j].Text))
            {
                continue;
            }

            try
            {
                if (regex.IsMatch(lines[j].Text))
                {
                    results.Add((pageNum, j, lines[j]));
                    
                    // Stop if we've reached the limit
                    if (results.Count >= maxMatches)
                    {
                        return;
                    }
                }
            }
            catch (Exception)
            {
                // Silently skip lines that cause regex match errors
                continue;
            }
        }
    }

    private void DisplayLine(int pageNum, int lineNum, MatchObject line)
    {
        AnsiConsole.Write(new Rule($"Page {pageNum + 1}, Line {lineNum + 1}").RuleStyle("blue"));

        var panel = new Panel(SafeMarkup(line.Text)).Header("Text").Expand();

        AnsiConsole.Write(panel);

        var table = new Table();
        table.AddColumn("Property");
        table.AddColumn("Value");

        table.AddRow("X0", line.X0.ToString());
        table.AddRow("Top", line.Top.ToString());
        table.AddRow("X1", line.X1.ToString());
        table.AddRow("Bottom", line.Bottom.ToString());

        if (line.Chars != null)
        {
            table.AddRow("Characters", line.Chars.Length.ToString());
        }

        AnsiConsole.Write(table);
    }

    // Helper method to get a description for an item
    private string GetItemDescription<T>(T item)
    {
        return item switch
        {
            Paper paper => $"{paper.Title} (DOI: {paper.Doi})",
            PdfData pdfData => GetEnhancedPdfDescription(pdfData),
            _ => item?.ToString() ?? "Unknown item",
        };
    }

    // New helper method to get enhanced PDF descriptions with metadata if available
    private string GetEnhancedPdfDescription(PdfData pdfData)
    {
        // First get the formatted filename
        string basicDescription = FormatPdfFileName(pdfData.FileName);
        
        // Now try to find matching paper metadata
        if (_paperCache.Count > 0)
        {
            // Extract DOI from filename if present
            string fileName = Path.GetFileNameWithoutExtension(pdfData.FileName);
            if (fileName.StartsWith("10.") && fileName.Contains('-'))
            {
                // This looks like a DOI-based filename
                string doi = fileName.Replace('-', '/');
                
                // Try to find matching paper by DOI
                var matchingPaper = _paperCache.FirstOrDefault(p => 
                    !string.IsNullOrEmpty(p.Doi) && 
                    p.Doi.Equals(doi, StringComparison.OrdinalIgnoreCase));
                
                if (matchingPaper != null)
                {
                    return $"{matchingPaper.Title} (DOI: {matchingPaper.Doi})";
                }
            }
            
            // If no DOI match, try by filename (some files might be named after the paper title)
            var possibleMatches = _paperCache
                .Where(p => !string.IsNullOrEmpty(p.Title) && 
                            fileName.Contains(p.Title, StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            if (possibleMatches.Count == 1)
            {
                return $"{possibleMatches[0].Title} (DOI: {possibleMatches[0].Doi})";
            }
        }
        
        // If no match found, return the basic description
        return basicDescription;
    }

    // Helper method to extract keywords from an expression
    private static HashSet<string> ExtractKeywordsFromExpression(string expression)
    {
        var keywords = new HashSet<string>();

        // Remove parentheses and operators to isolate potential keywords
        var simplified = expression
            .Replace("(", " ")
            .Replace(")", " ")
            .Replace(" AND ", " ")
            .Replace(" OR ", " ");

        // Split by comparison operators
        foreach (var op in new[] { ">=", "<=", ">", "<", "==" })
        {
            simplified = simplified.Replace(op, " ");
        }

        // Extract potential keywords
        var parts = simplified.Split([' '], StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            // If it's not a number, it's likely a keyword
            if (!int.TryParse(part, out _))
            {
                keywords.Add(part);
            }
        }

        return keywords;
    }

    // Helper method to load papers from metadata directory
    private List<Paper> LoadPapersFromMetadata(DirectoryInfo metadataDir)
    {
        var papers = new List<Paper>();

        foreach (var file in metadataDir.GetFiles("*.bin"))
        {
            try
            {
                var bin = File.ReadAllBytes(file.FullName);
                var paper = MemoryPackSerializer.Deserialize<Paper>(bin);
                if (paper != null)
                {
                    papers.Add(paper);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Error loading paper {FileName}: {Error}", file.Name, ex.Message);
            }
        }

        logger.LogInformation("Loaded {Count} papers", papers.Count);
        return papers;
    }

    // Helper method to display search results consistently
    private void DisplaySearchResults(
        List<(int PageNum, int LineNum, MatchObject Line)> results,
        string pattern,
        string pdfName = null,
        int? maxToShow = null
    )
    {
        // Safety check for null results
        if (results == null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Search results are null");
            return;
        }
        
        string escapedPattern = Markup.Escape(pattern);
        
        if (results.Count == 0)
        {
            string escapedPdfInfo = pdfName != null ? $" in {pdfName}" : "";
            AnsiConsole.MarkupLine($"[yellow]No matches found[/] for '{escapedPattern}'{escapedPdfInfo}");
            return;
        }

        if (pdfName == null)
        {
            AnsiConsole.Write(
                new Rule($"Found {results.Count} matches for '{escapedPattern}'").RuleStyle("green")
            );
        }

        // Ensure maxToShow is valid
        if (maxToShow.HasValue && maxToShow.Value <= 0)
        {
            maxToShow = 10; // Default to showing 10 results if an invalid value is provided
        }
        
        // Determine how many results to show
        int showCount = maxToShow.HasValue
            ? Math.Min(maxToShow.Value, results.Count)
            : results.Count;

        // Show the results
        var table = new Table();
        table.AddColumn("Page");
        table.AddColumn("Line");
        table.AddColumn("Text");

        for (int i = 0; i < showCount; i++)
        {
            try
            {
                // Safely extract values, with null checks
                if (i >= results.Count)
                {
                    break; // Guard against index out of bounds
                }
                
                var result = results[i];
                int pageIdx = result.PageNum;
                int line = result.LineNum;
                MatchObject matchLine = result.Line;
                
                if (matchLine == null)
                {
                    table.AddRow((pageIdx + 1).ToString(), (line + 1).ToString(), "[italic]<null line>[/]");
                    continue;
                }
                
                string text = matchLine.Text ?? "<null text>";
                
                table.AddRow(
                    (pageIdx + 1).ToString(), 
                    (line + 1).ToString(), 
                    Markup.Escape(text)
                );
            }
            catch (Exception ex)
            {
                // If any single result fails, log the error but continue with others
                table.AddRow("?", "?", $"[red]Error processing result: {SafeMarkup(ex.Message)}[/]");
            }
        }

        AnsiConsole.Write(table);

        // Show count of additional results if limited
        if (maxToShow.HasValue && results.Count > maxToShow.Value)
        {
            AnsiConsole.MarkupLine(
                $"[grey]... and {results.Count - maxToShow.Value} more matches[/]"
            );
        }
    }

    // Helper method to run the expression REPL with different data sources
    private void RunExpressionRepl<T>(
        List<T> items,
        Dictionary<T, Dictionary<string, int>> keywordCounts,
        string dataSourceName,
        CancellationToken cancellationToken
    )
        where T : class
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(
            new Rule($"Keyword Expression REPL for [yellow]{dataSourceName}[/]").RuleStyle("green")
        );

        var exampleTable = new Table().Border(TableBorder.Rounded);
        exampleTable.AddColumn("Examples").Centered();
        exampleTable.AddRow("'bug > 0'");
        exampleTable.AddRow("'test >= 3 OR confirm > 0'");

        var helpTable = new Table().Border(TableBorder.Simple);
        helpTable.AddColumn("Command").Centered();
        helpTable.AddColumn("Description");
        helpTable.AddRow("exit", "Quit the REPL");
        helpTable.AddRow("list", "Show available keywords");
        helpTable.AddRow("items", "Show loaded items");

        AnsiConsole.Write(
            new Panel(exampleTable)
                .Header("Enter expressions to evaluate against the data")
                .Padding(2, 1, 2, 1)
        );

        AnsiConsole.Write(helpTable);

        while (!cancellationToken.IsCancellationRequested)
        {
            var input = AnsiConsole.Prompt(new TextPrompt<string>("> ").PromptStyle("green"));

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (input.Equals("exit", StringComparison.CurrentCultureIgnoreCase))
                break;

            if (input.Equals("list", StringComparison.CurrentCultureIgnoreCase))
            {
                var allKeywords = keywordCounts
                    .Values.SelectMany(dict => dict.Keys)
                    .Distinct()
                    .OrderBy(k => k)
                    .ToList();

                var keywordTable = new Table();
                keywordTable.AddColumn("Available Keywords");

                foreach (var keyword in allKeywords)
                {
                    keywordTable.AddRow(keyword);
                }

                AnsiConsole.Write(keywordTable);
                continue;
            }

            if (input.Equals("items", StringComparison.CurrentCultureIgnoreCase))
            {
                var itemTable = new Table();
                itemTable.AddColumn("#");
                itemTable.AddColumn("Item");

                for (int i = 0; i < Math.Min(10, items.Count); i++)
                {
                    string itemDescription = SafeMarkup(GetItemDescription(items[i]));
                    itemTable.AddRow((i + 1).ToString(), itemDescription);
                }

                AnsiConsole.Write(new Rule($"Loaded Items ({items.Count})").RuleStyle("blue"));
                AnsiConsole.Write(itemTable);

                if (items.Count > 10)
                {
                    AnsiConsole.MarkupLine($"[grey]... and {items.Count - 10} more[/]");
                }
                continue;
            }

            try
            {
                // Extract keywords from the expression before parsing
                var expressionKeywords = ExtractKeywordsFromExpression(input);

                // Check if we need to compute any missing keywords
                var missingKeywords = new List<string>();
                foreach (var keyword in expressionKeywords)
                {
                    // Check if any item is missing this keyword in its counts
                    foreach (var item in items)
                    {
                        if (!keywordCounts[item].ContainsKey(keyword))
                        {
                            missingKeywords.Add(keyword);
                            break;
                        }
                    }
                }

                // If we have missing keywords, compute them for all items
                if (missingKeywords.Count > 0)
                {
                    AnsiConsole
                        .Status()
                        .Start(
                            $"Computing counts for new keywords: {string.Join(", ", missingKeywords)}",
                            ctx =>
                            {
                                foreach (var item in items)
                                {
                                    foreach (var keyword in missingKeywords)
                                    {
                                        // Only compute if not already present
                                        if (!keywordCounts[item].ContainsKey(keyword))
                                        {
                                            int count = CountKeywordInItem(item, keyword);
                                            keywordCounts[item][keyword] = count;
                                        }
                                    }
                                }
                            }
                        );
                }

                var evaluator = KeywordExpressionParser.ParseExpression(input);

                AnsiConsole.Write(new Rule($"Evaluating: {Markup.Escape(input)}").RuleStyle("blue"));

                int matchCount = 0;
                var resultsTable = new Table();
                resultsTable.AddColumn("Item");

                // Add columns for each keyword
                foreach (var keyword in expressionKeywords)
                {
                    resultsTable.AddColumn(keyword);
                }

                foreach (var item in items)
                {
                    var counts = keywordCounts[item];
                    var result = evaluator(counts);

                    if (result)
                    {
                        matchCount++;
                        string itemDescription = SafeMarkup(GetItemDescription(item));

                        var rowValues = new List<string> { itemDescription };

                        // Add counts for each keyword
                        foreach (var keyword in expressionKeywords)
                        {
                            if (counts.TryGetValue(keyword, out var count))
                            {
                                rowValues.Add(count.ToString());
                            }
                            else
                            {
                                rowValues.Add("0");
                            }
                        }

                        resultsTable.AddRow(rowValues.ToArray());
                    }
                }

                if (matchCount > 0)
                {
                    AnsiConsole.Write(resultsTable);
                }

                AnsiConsole.MarkupLine(
                    $"Expression matched [green]{matchCount}[/] out of [blue]{items.Count}[/] items ([yellow]{(double)matchCount / items.Count:P2}[/])"
                );
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {SafeMarkup(ex.Message)}");
            }
        }
    }

    // Helper method to count a keyword in an item
    private static int CountKeywordInItem<T>(T item, string keyword)
    {
        return item switch
        {
            Paper paper => PaperAnalyzer.CountKeywordInText(
                paper.Title + " " + paper.Abstract,
                keyword
            ),
            PdfData pdfData => PaperAnalyzer.CountKeywordInTexts(pdfData.Texts, keyword),
            _ => 0,
        };
    }

    // Helper method to safely markup text for AnsiConsole
    private static string SafeMarkup(string text)
    {
        return text.Replace("[", "[[").Replace("]", "]]");
    }

    // Helper method to format PDF filenames to be more readable
    private static string FormatPdfFileName(string fileName)
    {
        // Remove file extension
        string nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        
        // Try to clean up DOI-based filenames (like 10.1145-3597503.3608128.pdf)
        if (nameWithoutExtension.Contains('-') && nameWithoutExtension.StartsWith("10."))
        {
            return $"Paper DOI: {nameWithoutExtension.Replace('-', '/')}";
        }
        
        return nameWithoutExtension;
    }
}
