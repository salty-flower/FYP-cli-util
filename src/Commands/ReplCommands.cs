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
        var pdfDataDir = new DirectoryInfo(_pathsOptions.PdfDataDir);

        if (!pdfDataDir.Exists || pdfDataDir.GetFiles("*.bin").Length == 0)
        {
            logger.LogWarning("No PDF data found. Please run the analyze pdfs command first.");
            return;
        }

        logger.LogInformation("Loading PDF data...");
        var pdfDataList = new List<PdfData>();
        var pdfKeywordCounts = new Dictionary<PdfData, Dictionary<string, int>>();

        foreach (var file in pdfDataDir.GetFiles("*.bin"))
        {
            try
            {
                var bin = File.ReadAllBytes(file.FullName);
                var pdfData = MemoryPackSerializer.Deserialize<PdfData>(bin);
                if (pdfData != null)
                {
                    pdfDataList.Add(pdfData);

                    // Pre-compute keyword counts for all PDFs
                    pdfKeywordCounts[pdfData] = PaperAnalyzer.CountKeywordsInTexts(
                        pdfData.Texts,
                        _keywordsOptions.Analysis
                    );
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    "Error loading PDF data {FileName}: {Error}",
                    file.Name,
                    ex.Message
                );
            }
        }

        logger.LogInformation("Loaded {Count} PDF documents", pdfDataList.Count);

        if (pdfDataList.Count == 0)
        {
            logger.LogWarning("No PDF data could be loaded.");
            return;
        }

        RunExpressionRepl(pdfDataList, pdfKeywordCounts, "PDF content", cancellationToken);
    }

    /// <summary>
    /// Interactive REPL for inspecting PDF text lines
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public void TextLines(CancellationToken cancellationToken = default)
    {
        var pdfDataDir = new DirectoryInfo(_pathsOptions.PdfDataDir);

        if (!pdfDataDir.Exists || pdfDataDir.GetFiles("*.bin").Length == 0)
        {
            logger.LogWarning("No PDF data found. Please run the analyze pdfs command first.");
            return;
        }

        logger.LogInformation("Loading PDF data...");
        var pdfDataList = new List<PdfData>();

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
                    ex.Message
                );
            }
        }

        logger.LogInformation("Loaded {Count} PDF documents", pdfDataList.Count);

        if (pdfDataList.Count == 0)
        {
            logger.LogWarning("No PDF data could be loaded.");
            return;
        }

        // Ask user if they want to inspect a single PDF or work with all PDFs
        Console.WriteLine("How would you like to work with the PDFs?");
        Console.WriteLine("1. Inspect a single PDF");
        Console.WriteLine("2. Work with all PDFs");
        Console.Write("Enter your choice (1 or 2): ");
        
        if (!int.TryParse(Console.ReadLine(), out int modeChoice) || (modeChoice != 1 && modeChoice != 2))
        {
            logger.LogWarning("Invalid choice. Exiting.");
            return;
        }
        
        if (modeChoice == 1)
        {
            // Select a single PDF to inspect
            Console.WriteLine("\nAvailable PDFs:");
            for (int i = 0; i < pdfDataList.Count; i++)
            {
                Console.WriteLine($"{i + 1}. {pdfDataList[i].FileName}");
            }

            Console.Write("Select PDF number to inspect: ");
            if (!int.TryParse(Console.ReadLine(), out int pdfIndex) || pdfIndex < 1 || pdfIndex > pdfDataList.Count)
            {
                logger.LogWarning("Invalid selection. Exiting.");
                return;
            }

            var selectedPdf = pdfDataList[pdfIndex - 1];
            RunSinglePdfRepl(selectedPdf, pdfDataList, cancellationToken);
        }
        else
        {
            // Work with all PDFs
            RunAllPdfsRepl(pdfDataList, cancellationToken);
        }
    }

    private void RunSinglePdfRepl(PdfData pdfData, List<PdfData> allPdfData, CancellationToken cancellationToken)
    {
        Console.WriteLine($"TextLines REPL for {pdfData.FileName}");
        Console.WriteLine($"Document has {pdfData.TextLines.Length} pages");
        Console.WriteLine("Commands:");
        Console.WriteLine("  page <number> [line] - View all lines on a page or a specific line");
        Console.WriteLine("  search <pattern> [page] - Search text using regex pattern (optional: on specific page)");
        Console.WriteLine("  searchall <pattern> - Search text across all loaded PDFs");
        Console.WriteLine("  info - Show document information");
        Console.WriteLine("  exit - Exit REPL");

        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write("> ");
            var input = Console.ReadLine();

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
                        Console.WriteLine($"Unknown command: {parts[0]}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }

    private void RunAllPdfsRepl(List<PdfData> allPdfData, CancellationToken cancellationToken)
    {
        Console.WriteLine($"TextLines REPL for ALL PDFs ({allPdfData.Count} documents)");
        Console.WriteLine("Commands:");
        Console.WriteLine("  search <pattern> - Search text across all PDFs");
        Console.WriteLine("  list - List all available PDFs");
        Console.WriteLine("  select <number> - Select a specific PDF to inspect in detail");
        Console.WriteLine("  info - Show summary information about all PDFs");
        Console.WriteLine("  exit - Exit REPL");

        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write("> ");
            var input = Console.ReadLine();

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
                        Console.WriteLine($"Unknown command: {parts[0]}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }

    private void ListAllPdfs(List<PdfData> allPdfData)
    {
        Console.WriteLine($"Available PDFs ({allPdfData.Count}):");
        for (int i = 0; i < allPdfData.Count; i++)
        {
            Console.WriteLine($"{i + 1}. {allPdfData[i].FileName}");
        }
    }

    private void DisplayAllPdfsInfo(List<PdfData> allPdfData)
    {
        Console.WriteLine($"PDF Collection Summary:");
        Console.WriteLine($"Total PDFs: {allPdfData.Count}");
        
        // Count total pages
        int totalPages = allPdfData.Sum(pdf => pdf.TextLines.Length);
        Console.WriteLine($"Total Pages: {totalPages}");
        
        // Calculate average pages per document
        double avgPages = totalPages / (double)allPdfData.Count;
        Console.WriteLine($"Average Pages: {avgPages:F1}");
        
        // Show largest and smallest documents
        if (allPdfData.Count > 0)
        {
            var largest = allPdfData.OrderByDescending(pdf => pdf.TextLines.Length).First();
            var smallest = allPdfData.OrderBy(pdf => pdf.TextLines.Length).First();
            
            Console.WriteLine($"Largest Document: {largest.FileName} ({largest.TextLines.Length} pages)");
            Console.WriteLine($"Smallest Document: {smallest.FileName} ({smallest.TextLines.Length} pages)");
        }
    }

    private bool HandleSelectCommand(List<PdfData> allPdfData, string[] parts, CancellationToken cancellationToken)
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out int pdfIndex))
        {
            Console.WriteLine("Usage: select <number>");
            return false;
        }

        // Adjust for 1-based indexing that users see
        pdfIndex--;

        if (pdfIndex < 0 || pdfIndex >= allPdfData.Count)
        {
            Console.WriteLine($"Invalid PDF number. Valid range: 1-{allPdfData.Count}");
            return false;
        }

        var selectedPdf = allPdfData[pdfIndex];
        
        Console.WriteLine($"Selected {selectedPdf.FileName}");
        Console.WriteLine("1. Inspect this PDF in detail");
        Console.WriteLine("2. Cancel and return to all PDFs mode");
        Console.Write("Enter your choice (1 or 2): ");
        
        if (int.TryParse(Console.ReadLine(), out int choice) && choice == 1)
        {
            RunSinglePdfRepl(selectedPdf, allPdfData, cancellationToken);
            
            // Ask if the user wants to return to all PDFs mode or exit
            Console.WriteLine("Return to all PDFs mode? (y/n)");
            var response = Console.ReadLine()?.ToLowerInvariant();
            return response == "y" || response == "yes";
        }
        
        return false;
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
        Console.WriteLine($"Keyword Expression REPL for {dataSourceName}");
        Console.WriteLine("Enter expressions to evaluate against the data");
        Console.WriteLine("Examples: 'bug > 0', 'test >= 3 OR confirm > 0'");
        Console.WriteLine(
            "Type 'exit' to quit, 'list' to show available keywords, 'items' to show loaded items"
        );

        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write("> ");
            var input = Console.ReadLine();

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

                Console.WriteLine("Available keywords:");
                foreach (var keyword in allKeywords)
                {
                    Console.WriteLine($"  {keyword}");
                }
                continue;
            }

            if (input.Equals("items", StringComparison.CurrentCultureIgnoreCase))
            {
                Console.WriteLine($"Loaded items ({items.Count}):");
                for (int i = 0; i < Math.Min(10, items.Count); i++)
                {
                    string itemDescription = GetItemDescription(items[i]);
                    Console.WriteLine($"  {i + 1}. {itemDescription}");
                }
                if (items.Count > 10)
                {
                    Console.WriteLine($"  ... and {items.Count - 10} more");
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
                    Console.WriteLine(
                        $"Computing counts for new keywords: {string.Join(", ", missingKeywords)}"
                    );

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

                var evaluator = KeywordExpressionParser.ParseExpression(input);

                Console.WriteLine($"Evaluating: {input}");
                Console.WriteLine("Results:");

                int matchCount = 0;
                foreach (var item in items)
                {
                    var counts = keywordCounts[item];
                    var result = evaluator(counts);

                    if (result)
                    {
                        matchCount++;
                        string itemDescription = GetItemDescription(item);
                        Console.WriteLine($"  MATCH: {itemDescription}");

                        // Show the keyword counts that matched
                        foreach (var keyword in expressionKeywords)
                        {
                            if (counts.TryGetValue(keyword, out var count))
                            {
                                Console.WriteLine($"    {keyword}: {count}");
                            }
                            else
                            {
                                Console.WriteLine($"    {keyword}: 0");
                            }
                        }
                    }
                }

                Console.WriteLine(
                    $"Expression matched {matchCount} out of {items.Count} items ({(double)matchCount / items.Count:P2})"
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
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

    private void DisplayDocumentInfo(PdfData pdfData)
    {
        Console.WriteLine($"Document: {pdfData.FileName}");
        Console.WriteLine($"Number of pages: {pdfData.TextLines.Length}");
        
        // Display page statistics
        for (int i = 0; i < pdfData.TextLines.Length; i++)
        {
            Console.WriteLine($"  Page {i + 1}: {pdfData.TextLines[i].Length} lines");
        }
    }

    private void HandlePageCommand(PdfData pdfData, string[] parts)
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out int pageNum))
        {
            Console.WriteLine("Usage: page <number> [line]");
            return;
        }

        // Adjust for 0-based indexing
        pageNum--;

        if (pageNum < 0 || pageNum >= pdfData.TextLines.Length)
        {
            Console.WriteLine($"Invalid page number. Valid range: 1-{pdfData.TextLines.Length}");
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
                Console.WriteLine($"Invalid line number. Valid range: 1-{lines.Length}");
                return;
            }

            DisplayLine(pageNum, lineNum, lines[lineNum]);
        }
        else
        {
            // Display all lines on the page
            Console.WriteLine($"Page {pageNum + 1} ({lines.Length} lines):");
            for (int i = 0; i < lines.Length; i++)
            {
                Console.WriteLine($"  {i + 1}: {lines[i].Text}");
            }
        }
    }

    private void HandleSearchCommand(PdfData pdfData, string[] parts)
    {
        if (parts.Length < 2)
        {
            Console.WriteLine("Usage: search <pattern> [page]");
            return;
        }

        // Get the pattern (handling quoted patterns)
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

        // Check if a specific page is requested
        int? pageNum = null;
        if (parts.Length > 2 && int.TryParse(parts[parts.Length - 1], out int page))
        {
            pageNum = page - 1; // Adjust for 0-based indexing
            if (pageNum < 0 || pageNum >= pdfData.TextLines.Length)
            {
                Console.WriteLine($"Invalid page number. Valid range: 1-{pdfData.TextLines.Length}");
                return;
            }
        }

        try
        {
            var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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
            if (results.Count > 0)
            {
                Console.WriteLine($"Found {results.Count} matches for '{pattern}':");
                foreach (var (pageIdx, line, matchLine) in results)
                {
                    var matchText = matchLine.Text;
                    
                    // Add some context highlighting (if console supports it)
                    var match = regex.Match(matchText);
                    if (match.Success)
                    {
                        // Show match with some context
                        Console.WriteLine($"  Page {pageIdx + 1}, Line {line + 1}: {matchText}");
                    }
                    else
                    {
                        Console.WriteLine($"  Page {pageIdx + 1}, Line {line + 1}: {matchText}");
                    }
                }
            }
            else
            {
                Console.WriteLine($"No matches found for '{pattern}'");
            }
        }
        catch (System.Text.RegularExpressions.RegexParseException ex)
        {
            Console.WriteLine($"Invalid regex pattern: {ex.Message}");
        }
    }

    private void HandleSearchAllCommand(List<PdfData> allPdfData, string[] parts)
    {
        if (parts.Length < 2)
        {
            Console.WriteLine("Usage: searchall <pattern>");
            return;
        }

        // Get the pattern (handling quoted patterns)
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

        try
        {
            var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            Console.WriteLine($"Searching for '{pattern}' across {allPdfData.Count} PDFs...");
            
            // Track total matches
            int totalMatches = 0;
            int pdfWithMatches = 0;
            
            // Results from each PDF
            var allResults = new Dictionary<string, List<(int PageNum, int LineNum, MatchObject Line)>>();
            
            foreach (var pdf in allPdfData)
            {
                var results = new List<(int PageNum, int LineNum, MatchObject Line)>();
                
                for (int i = 0; i < pdf.TextLines.Length; i++)
                {
                    SearchInPage(pdf, regex, i, results);
                }
                
                if (results.Count > 0)
                {
                    allResults[pdf.FileName] = results;
                    totalMatches += results.Count;
                    pdfWithMatches++;
                }
            }
            
            // Display results
            if (totalMatches > 0)
            {
                Console.WriteLine($"Found {totalMatches} matches in {pdfWithMatches} PDFs");
                
                foreach (var pdfResult in allResults)
                {
                    Console.WriteLine($"\nPDF: {pdfResult.Key} ({pdfResult.Value.Count} matches)");
                    
                    // Limit number of matches shown per PDF to avoid overwhelming output
                    int maxToShow = Math.Min(pdfResult.Value.Count, 10);
                    for (int i = 0; i < maxToShow; i++)
                    {
                        var (pageIdx, line, matchLine) = pdfResult.Value[i];
                        Console.WriteLine($"  Page {pageIdx + 1}, Line {line + 1}: {matchLine.Text}");
                    }
                    
                    if (pdfResult.Value.Count > maxToShow)
                    {
                        Console.WriteLine($"  ... and {pdfResult.Value.Count - maxToShow} more matches");
                    }
                }
            }
            else
            {
                Console.WriteLine($"No matches found for '{pattern}' in any PDF");
            }
        }
        catch (System.Text.RegularExpressions.RegexParseException ex)
        {
            Console.WriteLine($"Invalid regex pattern: {ex.Message}");
        }
    }

    private void SearchInPage(
        PdfData pdfData, 
        System.Text.RegularExpressions.Regex regex, 
        int pageNum, 
        List<(int, int, MatchObject)> results)
    {
        var lines = pdfData.TextLines[pageNum];
        for (int j = 0; j < lines.Length; j++)
        {
            if (regex.IsMatch(lines[j].Text))
            {
                results.Add((pageNum, j, lines[j]));
            }
        }
    }

    private void DisplayLine(int pageNum, int lineNum, MatchObject line)
    {
        Console.WriteLine($"Page {pageNum + 1}, Line {lineNum + 1}:");
        Console.WriteLine($"  Text: {line.Text}");
        Console.WriteLine($"  Position: X0={line.X0}, Top={line.Top}, X1={line.X1}, Bottom={line.Bottom}");
        
        if (line.Chars != null)
        {
            Console.WriteLine($"  Characters: {line.Chars.Length} chars");
        }
    }

    // Helper method to get a description for an item
    private static string GetItemDescription<T>(T item) =>
        item switch
        {
            Paper paper => $"{paper.Title} (DOI: {paper.Doi})",
            PdfData pdfData => $"{pdfData.FileName}",
            _ => item?.ToString() ?? "Unknown item",
        };

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
}
