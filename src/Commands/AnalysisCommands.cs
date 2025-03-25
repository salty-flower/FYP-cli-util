using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ConsoleAppFramework;
using DataCollection.Commands.Repl;
using DataCollection.Models.Export;
using DataCollection.Options;
using Microsoft.Extensions.Logging;

namespace DataCollection.Commands;

/// <summary>
/// Commands for analyzing research papers
/// </summary>
[RegisterCommands("procedure")]
[ConsoleAppFilter<PathsOptions.Filter>]
public class AnalysisCommands(
    ILogger<AnalysisCommands> logger,
    TextLinesReplCommand textLinesRepl,
    MetadataReplCommand metadataRepl
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
}
