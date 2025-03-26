using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ConsoleAppFramework;
using DataCollection.Commands.Repl;
using DataCollection.Filters;
using DataCollection.Models.Export;
using DataCollection.Options;
using DataCollection.Services;
using DataCollection.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Commands;

/// <summary>
/// Commands for analyzing research papers
/// </summary>
[RegisterCommands("procedure")]
[ConsoleAppFilter<PathsOptions.Filter>]
public class AnalysisCommands(
    ILogger<AnalysisCommands> logger,
    TextLinesReplCommand textLinesRepl,
    MetadataReplCommand metadataRepl,
    DataLoadingService dataLoadingService,
    IOptions<PathsOptions> pathsOptions
)
{
    /// <summary>
    /// Filter papers for bug tables and testing technique mentions
    /// </summary>
    /// <param name="bugTablesPattern">Regex pattern to detect bug tables</param>
    /// <param name="techniquesPattern">Regex pattern to detect testing techniques</param>
    /// <param name="tempBugTablesFile">Temporary file for bug tables results</param>
    /// <param name="tempTechniquesFile">Temporary file for techniques results</param>
    /// <param name="outputFile">Output file for the final analysis</param>
    /// <param name="keepTempFiles">Whether to keep temporary files</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of papers that have both bug tables and techniques</returns>
    public async Task<int> FilterTechniqueAndBugs(
        string bugTablesPattern = @"Table\d+:*[^\s]*bugs",
        string techniquesPattern =
            @"(differential.{1}testing|metamorphic.{1}testing|property-based|fuzzing)",
        string tempBugTablesFile = "bug-tables.json",
        string tempTechniquesFile = "techniques.json",
        string outputFile = "research-analysis.json",
        bool keepTempFiles = false,
        CancellationToken cancellationToken = default
    )
    {
        logger.LogInformation("Starting analysis process...");

        // Step 1: Search for bug-related tables in PDFs
        logger.LogInformation("Searching for bug tables in PDFs...");

        // Call the appropriate ReplCommands method with out parameter
        TextLinesSearchResult bugTablesData;
        int bugTablesResult = textLinesRepl.RunNonInteractiveSearch(
            bugTablesPattern,
            out bugTablesData,
            tempBugTablesFile,
            cancellationToken
        );

        if (bugTablesData == null)
        {
            logger.LogError("Error searching for bug tables - no results returned");
            return 1;
        }

        // Step 2: Search for testing techniques in metadata
        logger.LogInformation("Searching for testing techniques in metadata...");

        // Call the appropriate ReplCommands method with out parameter
        MetadataSearchResult techniquesData;
        int techniquesResult = metadataRepl.RunNonInteractiveSearch(
            techniquesPattern,
            out techniquesData,
            tempTechniquesFile,
            cancellationToken
        );

        if (techniquesData == null)
        {
            logger.LogError("Error searching for testing techniques - no results returned");
            return 1;
        }

        // Step 3: Process and merge the results
        logger.LogInformation("Processing and merging results...");

        try
        {
            // No need to deserialize, we already have the data in memory!
            logger.LogInformation(
                "Found {BugTables} bug tables across {PdfsWithMatches} PDFs",
                bugTablesData.TotalMatches,
                bugTablesData.PdfsWithMatches
            );

            logger.LogInformation(
                "Found {TechniqueMatches} technique mentions",
                techniquesData.TotalMatches
            );

            // Process bug tables data to standardize DOI format
            var bugTablesByDoi = new Dictionary<string, BugTableInfo>();
            foreach (var item in bugTablesData.Results)
            {
                var doi = item.FileName.Replace(".pdf", "").Replace('-', '/');
                bugTablesByDoi[doi] = new BugTableInfo
                {
                    Title = item.Pdf,
                    TableCount = item.ResultCount,
                    Tables = item.Results.Select(r => r.Text).ToList(),
                };
            }

            // Find papers that exist in both datasets
            var intersection = techniquesData
                .Results.Where(technique => bugTablesByDoi.ContainsKey(technique.Paper.DOI))
                .Select(technique => new PaperAnalysisResult
                {
                    Title = technique.Paper.Title,
                    Doi = technique.Paper.DOI,
                    Authors =
                        technique.Paper.Authors != null
                            ? new List<string>(technique.Paper.Authors)
                            : new List<string>(),
                    Technique = technique.Match,
                    TechniqueContext = technique.Context,
                    BugTables = new BugTableSummary
                    {
                        Count = bugTablesByDoi[technique.Paper.DOI].TableCount,
                        Tables = bugTablesByDoi[technique.Paper.DOI].Tables,
                    },
                })
                .ToList();

            // Step 4: Merge entries for the same paper
            logger.LogInformation("Merging entries for the same paper...");
            var mergedPapers = intersection
                .GroupBy(p => p.Doi)
                .Select(group =>
                {
                    var firstItem = group.First();

                    // Create arrays of unique techniques and contexts
                    var techniques = group.Select(g => g.Technique).Distinct().ToList();
                    var techniqueContexts = group
                        .Select(g => new TechniqueContext
                        {
                            Technique = g.Technique,
                            Context = g.TechniqueContext,
                        })
                        .ToList();

                    // Create a new merged object
                    return new MergedPaperAnalysis
                    {
                        Title = firstItem.Title,
                        Doi = firstItem.Doi,
                        Authors = firstItem.Authors,
                        Techniques = techniques,
                        TechniqueContexts = techniqueContexts,
                        BugTables = firstItem.BugTables,
                    };
                })
                .OrderBy(p => p.Title)
                .ToList();

            // Create final output
            var output = new AnalysisOutput
            {
                Summary = new AnalysisSummary
                {
                    TotalPapers = mergedPapers.Count,
                    Timestamp = DateTime.Now.ToString("o"),
                    BugTablePattern = bugTablesPattern,
                    TechniquesPattern = techniquesPattern,
                },
                Papers = mergedPapers,
            };

            // Save to file
            await File.WriteAllTextAsync(
                outputFile,
                JsonSerializer.Serialize(output, ExportModelJsonContext.Default.AnalysisOutput),
                cancellationToken
            );

            logger.LogInformation("Analysis complete! Results saved to {OutputFile}", outputFile);
            logger.LogInformation(
                "Found {PaperCount} papers that contain both bug tables and testing techniques",
                mergedPapers.Count
            );

            // Clean up temporary files if not keeping them
            if (!keepTempFiles)
            {
                if (File.Exists(tempBugTablesFile))
                    File.Delete(tempBugTablesFile);

                if (File.Exists(tempTechniquesFile))
                    File.Delete(tempTechniquesFile);

                logger.LogInformation("Temporary files removed");
            }

            // Display technique summary
            var techniqueSummary = mergedPapers
                .SelectMany(p => p.Techniques)
                .GroupBy(t => t)
                .OrderByDescending(g => g.Count());

            logger.LogInformation("Testing Technique Summary:");
            foreach (var tech in techniqueSummary)
            {
                logger.LogInformation("  {key}: {count} papers", tech.Key, tech.Count());
            }

            return mergedPapers.Count;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing results");
            return 1;
        }
    }

    /// <summary>
    /// Analyze bug terminology across all papers
    /// </summary>
    /// <param name="bugPattern">Regex pattern to detect bug sentences</param>
    /// <param name="outputFile">Output file for the terminology analysis</param>
    /// <param name="adjectivesOnly">Whether to filter for adjectives only</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of sentences containing "bug" found across all papers</returns>

    [ConsoleAppFilter<PythonEngineInitFilter>]
    public async Task<int> AnalyzeBugTerminology(
        string bugPattern = @"\b(?:bug|bugs)\b",
        string outputFile = "bug-terminology-analysis.json",
        bool adjectivesOnly = false,
        CancellationToken cancellationToken = default
    )
    {
        logger.LogInformation("Starting bug terminology analysis...");

        if (adjectivesOnly)
            logger.LogInformation("Filtering for adjectives only using NLTK");

        var paths = pathsOptions.Value;

        // Step 1: Load all the PDF data
        logger.LogInformation("Loading PDF data from directory...");
        var pdfDataList = dataLoadingService.LoadPdfDataFromDirectory(
            paths.PdfDataDir,
            paths.PaperMetadataDir
        );

        if (pdfDataList.Count == 0)
        {
            logger.LogWarning(
                "No PDF data could be loaded. Please run the analyze pdfs command first."
            );
            return 0;
        }

        logger.LogInformation("Loaded {Count} papers for analysis", pdfDataList.Count);

        // Step 2: Create the results structure
        var results = new BugTerminologyAnalysis
        {
            Summary = new BugTerminologySummary
            {
                TotalPapers = pdfDataList.Count,
                SearchPattern = bugPattern,
                Timestamp = DateTime.Now.ToString("o"),
                AdjectivesOnly = adjectivesOnly,
            },
            PaperAnalyses = new List<PaperBugTerminologyAnalysis>(),
            GlobalWordFrequency = new Dictionary<string, int>(),
        };

        var regex = new Regex(bugPattern, RegexOptions.IgnoreCase);
        int totalBugSentences = 0;

        // Step 3: Process each paper
        foreach (var pdfData in pdfDataList)
        {
            string paperTitle = Path.GetFileNameWithoutExtension(pdfData.FileName);
            string paperDoi = string.Empty;

            // Try to get paper metadata from filename
            if (paperTitle.StartsWith("10.") && paperTitle.Contains('-'))
            {
                paperDoi = paperTitle.Replace('-', '/');
                paperTitle = PdfDescriptionService.FormatPdfFileName(pdfData.FileName);
            }

            logger.LogInformation("Processing paper: {PaperTitle}", paperTitle);

            // Create paper analysis object
            var paperAnalysis = new PaperBugTerminologyAnalysis
            {
                Title = paperTitle,
                FileName = pdfData.FileName,
                DOI = paperDoi,
                BugSentences = new List<BugSentence>(),
                WordFrequency = new Dictionary<string, int>(),
            };

            // Use the PdfTextUtils to extract bug sentences with optional adjective filtering
            int paperBugSentences = PdfTextUtils.ExtractBugSentences(
                pdfData,
                regex,
                paperAnalysis,
                adjectivesOnly
            );

            totalBugSentences += paperBugSentences;

            // Add paper-level word frequency to global word frequency
            foreach (var word in paperAnalysis.WordFrequency)
            {
                if (results.GlobalWordFrequency.ContainsKey(word.Key))
                    results.GlobalWordFrequency[word.Key] += word.Value;
                else
                    results.GlobalWordFrequency[word.Key] = word.Value;
            }

            // Sort the word frequency dictionaries by frequency (descending)
            paperAnalysis.WordFrequency = paperAnalysis
                .WordFrequency.OrderByDescending(pair => pair.Value)
                .ToDictionary(pair => pair.Key, pair => pair.Value);

            // Add paper analysis to results
            paperAnalysis.TotalBugSentences = paperAnalysis.BugSentences.Count;
            results.PaperAnalyses.Add(paperAnalysis);
        }

        // Step 4: Update summary information
        results.Summary.TotalBugSentences = totalBugSentences;
        results.Summary.PapersWithBugs = results.PaperAnalyses.Count(p => p.TotalBugSentences > 0);

        // Sort the global word frequency dictionary by frequency (descending)
        results.GlobalWordFrequency = results
            .GlobalWordFrequency.OrderByDescending(pair => pair.Value)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        // Step 5: Save results to file
        string jsonString = JsonSerializer.Serialize(
            results,
            ExportModelJsonContext.Default.BugTerminologyAnalysis
        );

        await File.WriteAllTextAsync(outputFile, jsonString, cancellationToken);

        // Step 6: Display summary
        logger.LogInformation("Bug terminology analysis complete!");
        logger.LogInformation(
            "Found {TotalSentences} sentences containing 'bug' across {PapersWithBugs} papers out of {TotalPapers} total papers{AdjectiveFilter}",
            totalBugSentences,
            results.Summary.PapersWithBugs,
            results.Summary.TotalPapers,
            adjectivesOnly ? " (adjectives only)" : ""
        );

        // Top 10 most frequent words
        logger.LogInformation(
            "Top 10 most frequent words in bug sentences{AdjectiveFilter}:",
            adjectivesOnly ? " (adjectives only)" : ""
        );
        foreach (var word in results.GlobalWordFrequency.Take(10))
        {
            logger.LogInformation("  {Word}: {Count}", word.Key, word.Value);
        }

        logger.LogInformation("Results saved to {OutputFile}", outputFile);

        return totalBugSentences;
    }

    /// <summary>
    /// Merge and analyze bug terminology analysis across multiple jobs/directories
    /// and export the results to CSV files in a specified directory
    /// </summary>
    /// <param name="analysisPattern">File pattern to search for bug terminology analysis JSON files (default: "bug-terminology-analysis.json")</param>
    /// <param name="outputDirectory">Directory path where CSV files will be saved - will be created if it doesn't exist</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of analysis files processed</returns>
    public async Task<int> MergeBugTerminologyAnalysis(
        string analysisPattern = "bug-terminology-analysis.json",
        string outputDirectory = "analysis-results",
        CancellationToken cancellationToken = default
    )
    {
        logger.LogInformation("Starting to merge bug terminology analysis files...");

        // Use default directory if empty
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            outputDirectory = "analysis-results";
            logger.LogInformation(
                "Using default output directory: {outputDirectory}",
                outputDirectory
            );
        }

        // Create output directory if it doesn't exist
        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
            logger.LogInformation("Created output directory: {outputDirectory}", outputDirectory);
        }

        // Find all bug terminology analysis files
        var analysisFiles = Directory.GetFiles(
            pathsOptions.Value.BaseDir,
            analysisPattern,
            SearchOption.AllDirectories
        );

        if (analysisFiles.Length == 0)
        {
            logger.LogWarning(
                "No bug terminology analysis files found matching pattern: {pattern}",
                analysisPattern
            );
            return 0;
        }

        logger.LogInformation("Found {count} bug terminology analysis files", analysisFiles.Length);

        // Prepare data structures to store merged results
        var globalWordFrequency = new Dictionary<string, int>();
        var jobSummaries = new List<(string JobName, int TotalWordFrequency)>();
        var jobWordFrequencies = new Dictionary<string, Dictionary<string, int>>();

        // Process each analysis file
        foreach (var file in analysisFiles)
        {
            try
            {
                // Read and parse the JSON file
                var jsonString = await File.ReadAllTextAsync(file, cancellationToken);
                var analysis = JsonSerializer.Deserialize(
                    jsonString,
                    ExportModelJsonContext.Default.BugTerminologyAnalysis
                );

                if (analysis == null)
                {
                    logger.LogWarning("Could not parse analysis file: {file}", file);
                    continue;
                }

                // Get job name from parent directory
                var jobName = Path.GetFileName(Path.GetDirectoryName(file));

                // Skip if we can't determine job name
                if (string.IsNullOrWhiteSpace(jobName))
                    jobName = Path.GetFileNameWithoutExtension(file);

                logger.LogInformation("Processing job: {jobName}", jobName);

                // Calculate total word frequency for this job
                var totalFrequency = analysis.GlobalWordFrequency.Values.Sum();
                jobSummaries.Add((jobName, totalFrequency));

                // Store job-specific word frequency
                jobWordFrequencies[jobName] = analysis.GlobalWordFrequency;

                // Merge into global word frequency
                foreach (var word in analysis.GlobalWordFrequency)
                {
                    if (globalWordFrequency.ContainsKey(word.Key))
                        globalWordFrequency[word.Key] += word.Value;
                    else
                        globalWordFrequency[word.Key] = word.Value;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing file: {file}", file);
            }
        }

        // Sort the global word frequency by count (descending)
        var sortedGlobalWordFrequency = globalWordFrequency
            .OrderByDescending(pair => pair.Value)
            .ToList();

        // Export CSV files

        // 1. Global summary (word frequencies across all jobs)
        var globalSummaryPath = Path.Combine(outputDirectory, "global-summary.csv");
        using (var writer = new StreamWriter(globalSummaryPath))
        {
            await writer.WriteLineAsync("Word,TotalFrequency");
            foreach (var pair in sortedGlobalWordFrequency)
                await writer.WriteLineAsync($"{EscapeCsvField(pair.Key)},{pair.Value}");
        }
        logger.LogInformation("Global word frequency summary saved to {path}", globalSummaryPath);

        // 2. Job summary (total word frequency per job)
        var jobSummaryPath = Path.Combine(outputDirectory, "job-summary.csv");
        using (var writer = new StreamWriter(jobSummaryPath))
        {
            await writer.WriteLineAsync("JobName,TotalWordFrequency");
            foreach (var (jobName, totalFrequency) in jobSummaries.OrderBy(j => j.JobName))
            {
                await writer.WriteLineAsync($"{EscapeCsvField(jobName)},{totalFrequency}");
            }
        }
        logger.LogInformation("Job summary saved to {path}", jobSummaryPath);

        // 3. Individual job word frequencies
        foreach (var job in jobWordFrequencies)
        {
            var jobName = job.Key;
            var wordFrequency = job.Value.OrderByDescending(pair => pair.Value);

            var jobPath = Path.Combine(outputDirectory, $"{jobName}.csv");
            using (var writer = new StreamWriter(jobPath))
            {
                await writer.WriteLineAsync("Word,Frequency");
                foreach (var pair in wordFrequency)
                {
                    await writer.WriteLineAsync($"{EscapeCsvField(pair.Key)},{pair.Value}");
                }
            }
            logger.LogInformation(
                "Word frequency for job {jobName} saved to {path}",
                jobName,
                jobPath
            );
        }

        logger.LogInformation(
            "Merge and analysis complete! {count} files processed\n"
                + "Results exported to CSV files in directory: {directory}\n"
                + "Generated CSV files:\n"
                + "  - global-summary.csv (Word frequencies across all jobs)\n"
                + "  - job-summary.csv (Total word counts per job)\n"
                + "  - [jobname].csv (Individual job word frequencies)",
            analysisFiles.Length,
            outputDirectory
        );

        // Display summary statistics
        logger.LogInformation(
            "Top 10 most frequent words across all jobs:\n" + "{Words}",
            string.Join(
                "\n",
                sortedGlobalWordFrequency.Take(10).Select(pair => $"  {pair.Key}: {pair.Value}")
            )
        );

        return analysisFiles.Length;
    }

    /// <summary>
    /// Helper method to escape CSV field values that might contain commas, quotes, or newlines
    /// </summary>
    /// <param name="field">The field value to escape</param>
    /// <returns>Properly escaped CSV field value</returns>
    private string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return string.Empty;
        }

        // If the field contains commas, quotes, or newlines, wrap it in quotes and escape internal quotes
        bool needsQuotes =
            field.Contains(',')
            || field.Contains('"')
            || field.Contains('\n')
            || field.Contains('\r');

        if (needsQuotes)
        {
            // Double up any quotes within the field
            field = field.Replace("\"", "\"\"");
            return $"\"{field}\"";
        }

        return field;
    }
}
