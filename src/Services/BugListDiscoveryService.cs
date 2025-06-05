using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataCollection.Models;
using DataCollection.Models.Export.BugAnalysis;
using DataCollection.Options;
using DataCollection.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Services;

/// <summary>
/// Orchestrator service for discovering bug lists and artifact repositories from papers
/// </summary>
public class BugListDiscoveryService
{
    private readonly PdfContentAnalysisService pdfAnalysisService;
    private readonly RepositoryAnalysisService repositoryAnalysisService;
    private readonly WebSearchAnalysisService webSearchAnalysisService;
    private readonly ILogger<BugListDiscoveryService> logger;
    private readonly IOptions<PathsOptions> pathsOptions;

    public BugListDiscoveryService(
        PdfContentAnalysisService pdfAnalysisService,
        RepositoryAnalysisService repositoryAnalysisService,
        WebSearchAnalysisService webSearchAnalysisService,
        ILogger<BugListDiscoveryService> logger,
        IOptions<PathsOptions> pathsOptions
    )
    {
        this.pdfAnalysisService = pdfAnalysisService;
        this.repositoryAnalysisService = repositoryAnalysisService;
        this.webSearchAnalysisService = webSearchAnalysisService;
        this.logger = logger;
        this.pathsOptions = pathsOptions;
    }

    /// <summary>
    /// Main method to discover bug lists and artifacts for a given paper
    /// </summary>
    public async Task<BugListDiscoveryResult> DiscoverBugListsAsync(
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        logger.LogInformation(
            "Starting bug list discovery for paper {PaperDoi}: {PaperTitle}",
            paper.Doi,
            paper.Title
        );

        var result = new BugListDiscoveryResult
        {
            Doi = paper.Doi,
            Title = paper.Title,
            BugLists = [],
            ArtifactRepositories = [],
        };

        try
        {
            // Phase 1: Extract from PDF content
            await ExtractFromPdfContent(paper, result, cancellationToken);

            // Phase 2: Explore repositories for additional bug lists
            await ExploreRepositories(result, paper, cancellationToken);

            // Phase 3: Perform web search if needed
            await PerformWebSearchIfNeeded(paper, result, cancellationToken);

            // Phase 4: Post-process and validate results
            await PostProcessResults(result, paper, cancellationToken);

            LogFinalResults(paper, result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to discover bug lists for paper {PaperDoi}", paper.Doi);
        }

        return result;
    }

    /// <summary>
    /// Phase 1: Extract bug lists and artifacts from PDF content
    /// </summary>
    private async Task ExtractFromPdfContent(
        Paper paper,
        BugListDiscoveryResult result,
        CancellationToken cancellationToken
    )
    {
        logger.LogDebug("Phase 1: Extracting from PDF content for paper {PaperDoi}", paper.Doi);

        try
        {
            await pdfAnalysisService.ExtractFromPdfContent(paper, result, cancellationToken);

            logger.LogInformation(
                "PDF analysis completed for paper {PaperDoi}. Found {BugListCount} bug lists, {RepoCount} repositories",
                paper.Doi,
                result.BugLists.Count,
                result.ArtifactRepositories.Count
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PDF content analysis failed for paper {PaperDoi}", paper.Doi);
        }
    }

    /// <summary>
    /// Phase 2: Explore repositories to find additional bug lists
    /// </summary>
    private async Task ExploreRepositories(
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        if (result.ArtifactRepositories.Count == 0)
        {
            logger.LogDebug("Phase 2: Skipping repository exploration - no repositories found");
            return;
        }

        logger.LogDebug(
            "Phase 2: Exploring {RepoCount} repositories for paper {PaperDoi}",
            result.ArtifactRepositories.Count,
            paper.Doi
        );

        try
        {
            await repositoryAnalysisService.ExploreRepositoryBugLists(
                result,
                paper,
                cancellationToken
            );

            logger.LogInformation(
                "Repository exploration completed for paper {PaperDoi}. Total bug lists: {BugListCount}",
                paper.Doi,
                result.BugLists.Count
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Repository exploration failed for paper {PaperDoi}", paper.Doi);
        }
    }

    /// <summary>
    /// Phase 3: Perform web search if we don't have sufficient results
    /// </summary>
    private async Task PerformWebSearchIfNeeded(
        Paper paper,
        BugListDiscoveryResult result,
        CancellationToken cancellationToken
    )
    {
        if (!webSearchAnalysisService.ShouldPerformWebSearch(result))
        {
            logger.LogDebug(
                "Phase 3: Skipping web search - sufficient high-confidence artifacts found"
            );
            return;
        }

        logger.LogDebug("Phase 3: Performing web search for paper {PaperDoi}", paper.Doi);

        try
        {
            var searchPerformed = await webSearchAnalysisService.SearchForArtifactsIfNeeded(
                paper,
                result,
                cancellationToken
            );

            if (searchPerformed)
            {
                logger.LogInformation(
                    "Web search completed for paper {PaperDoi}. Total artifacts: {RepoCount}",
                    paper.Doi,
                    result.ArtifactRepositories.Count
                );
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Web search failed for paper {PaperDoi}", paper.Doi);
        }
    }

    /// <summary>
    /// Phase 4: Post-process results, remove duplicates, validate confidence scores
    /// </summary>
    private async Task PostProcessResults(
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        logger.LogDebug("Phase 4: Post-processing results for paper {PaperDoi}", paper.Doi);

        try
        {
            // Remove duplicate bug lists
            var uniqueBugLists = result
                .BugLists.GroupBy(bl => bl.Url.ToLowerInvariant())
                .Select(g => g.OrderByDescending(bl => bl.Confidence).First())
                .ToList();

            result.BugLists.Clear();
            result.BugLists.AddRange(uniqueBugLists);

            // Remove duplicate repositories
            var uniqueRepositories = result
                .ArtifactRepositories.GroupBy(ar => ar.Url.ToLowerInvariant())
                .Select(g => g.OrderByDescending(ar => ar.Confidence).First())
                .ToList();

            result.ArtifactRepositories.Clear();
            result.ArtifactRepositories.AddRange(uniqueRepositories);

            // Filter by minimum confidence
            result.BugLists.RemoveAll(bl =>
                bl.Confidence < BugListConstants.HighConfidenceThreshold * 0.5
            );
            result.ArtifactRepositories.RemoveAll(ar =>
                ar.Confidence < BugListConstants.HighConfidenceThreshold * 0.4
            );

            logger.LogDebug(
                "Post-processing completed. Final counts - Bug lists: {BugListCount}, Repositories: {RepoCount}",
                result.BugLists.Count,
                result.ArtifactRepositories.Count
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Post-processing failed for paper {PaperDoi}", paper.Doi);
        }
    }

    /// <summary>
    /// Log final results summary
    /// </summary>
    private void LogFinalResults(Paper paper, BugListDiscoveryResult result)
    {
        logger.LogInformation(
            "Bug list discovery completed for paper {PaperDoi}. "
                + "Found {BugListCount} bug lists and {RepoCount} artifact repositories. "
                + "High-confidence bug lists: {HighConfidenceBugLists}, High-confidence repositories: {HighConfidenceRepos}",
            paper.Doi,
            result.BugLists.Count,
            result.ArtifactRepositories.Count,
            result.BugLists.Count(bl => bl.Confidence >= BugListConstants.HighConfidenceThreshold),
            result.ArtifactRepositories.Count(ar =>
                ar.Confidence >= BugListConstants.HighConfidenceThreshold
            )
        );

        // Log details for high-confidence findings
        var highConfidenceBugLists = result
            .BugLists.Where(bl => bl.Confidence >= BugListConstants.HighConfidenceThreshold)
            .ToList();
        if (highConfidenceBugLists.Count > 0)
        {
            logger.LogInformation(
                "High-confidence bug lists for {PaperDoi}: {BugLists}",
                paper.Doi,
                string.Join(", ", highConfidenceBugLists.Select(bl => bl.Url).Take(3))
            );
        }

        var highConfidenceRepos = result
            .ArtifactRepositories.Where(ar =>
                ar.Confidence >= BugListConstants.HighConfidenceThreshold
            )
            .ToList();
        if (highConfidenceRepos.Count > 0)
        {
            logger.LogInformation(
                "High-confidence artifact repositories for {PaperDoi}: {Repositories}",
                paper.Doi,
                string.Join(", ", highConfidenceRepos.Select(ar => ar.Url).Take(3))
            );
        }
    }

    /// <summary>
    /// Discover bug lists for multiple papers and return complete analysis
    /// </summary>
    public async Task<BugListDiscoveryAnalysis> DiscoverBugListsAsync(
        List<string> dois,
        CancellationToken cancellationToken
    )
    {
        var results = new List<BugListDiscoveryResult>();

        foreach (var doi in dois)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var paper = new Paper
            {
                Doi = doi,
                Title = doi,
                Authors = [],
                Abstract = "",
                Url = "",
            }; // Simplified paper object
            var result = await DiscoverBugListsAsync(paper, cancellationToken);
            results.Add(result);
        }

        var summary = GetDiscoverySummary(results);

        return new BugListDiscoveryAnalysis
        {
            Summary = summary,
            PaperResults = results,
            BugTrackingSystemStats = CalculateBugTrackingStats(results),
            RepositoryTypeStats = CalculateRepositoryTypeStats(results),
        };
    }

    /// <summary>
    /// Get discovery statistics for a batch of papers
    /// </summary>
    public BugListDiscoverySummary GetDiscoverySummary(List<BugListDiscoveryResult> results)
    {
        if (results.Count == 0)
        {
            return new BugListDiscoverySummary
            {
                TotalPapersProcessed = 0,
                PapersWithBugLists = 0,
                PapersWithArtifacts = 0,
                TotalBugListsFound = 0,
                TotalArtifactsFound = 0,
                SuccessRate = 0.0,
            };
        }

        var papersWithBugLists = results.Count(r => r.BugLists.Count > 0);
        var papersWithArtifacts = results.Count(r => r.ArtifactRepositories.Count > 0);
        var papersWithHighConfidenceFindings = results.Count(r =>
            r.BugLists.Any(bl => bl.Confidence >= BugListConstants.HighConfidenceThreshold)
            || r.ArtifactRepositories.Any(ar =>
                ar.Confidence >= BugListConstants.HighConfidenceThreshold
            )
        );

        return new BugListDiscoverySummary
        {
            TotalPapersProcessed = results.Count,
            PapersWithBugLists = papersWithBugLists,
            PapersWithArtifacts = papersWithArtifacts,
            TotalBugListsFound = results.Sum(r => r.BugLists.Count),
            TotalArtifactsFound = results.Sum(r => r.ArtifactRepositories.Count),
            SuccessRate = (double)papersWithHighConfidenceFindings / results.Count,
        };
    }

    private Dictionary<string, int> CalculateBugTrackingStats(List<BugListDiscoveryResult> results)
    {
        var stats = new Dictionary<string, int>();

        foreach (var result in results)
        {
            foreach (var bugList in result.BugLists)
            {
                if (stats.ContainsKey(bugList.Type))
                    stats[bugList.Type]++;
                else
                    stats[bugList.Type] = 1;
            }
        }

        return stats;
    }

    private Dictionary<string, int> CalculateRepositoryTypeStats(
        List<BugListDiscoveryResult> results
    )
    {
        var stats = new Dictionary<string, int>();

        foreach (var result in results)
        {
            foreach (var repo in result.ArtifactRepositories)
            {
                if (stats.ContainsKey(repo.Type))
                    stats[repo.Type]++;
                else
                    stats[repo.Type] = 1;
            }
        }

        return stats;
    }
}
