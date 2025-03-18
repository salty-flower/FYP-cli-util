using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ConsoleAppFramework;
using DataCollection.Models;
using DataCollection.Options;
using DataCollection.Services;
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
