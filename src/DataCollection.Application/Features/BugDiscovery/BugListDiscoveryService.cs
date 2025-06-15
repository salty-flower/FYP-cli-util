using DataCollection.Application.Features.PaperAnalysis;
using DataCollection.Application.Options;
using DataCollection.Core.Models;
using DataCollection.Infrastructure.Models.BugList;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Application.Features.BugDiscovery;

public class BugListDiscoveryService(
    PdfContentAnalysisService pdfAnalysisService,
    RepositoryAnalysisService repositoryAnalysisService,
    WebSearchAnalysisService webSearchAnalysisService,
    IOptionsSnapshot<ThresholdOptions> thresholdOptions,
    ILogger<BugListDiscoveryService> logger
)
{
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

        // Phase 1: Extract from PDF content
        var pdfResult = await pdfAnalysisService.ExtractFromPdfContent(paper, cancellationToken);
        logger.LogInformation(
            "PDF analysis found {BugCount} bug lists and {RepoCount} repositories.",
            pdfResult.BugLists.Count,
            pdfResult.ArtifactRepositories.Count
        );

        // Phase 2: Explore repositories found in the PDF
        var repoResult = await repositoryAnalysisService.ExploreRepositoryBugLists(
            pdfResult.ArtifactRepositories,
            paper,
            cancellationToken
        );
        logger.LogInformation(
            "Repository analysis found {BugCount} additional bug lists.",
            repoResult.BugLists.Count
        );

        // Combine initial findings
        var combinedBugLists = pdfResult.BugLists.Concat(repoResult.BugLists).ToList();
        var combinedRepositories = repoResult.ArtifactRepositories; // Repos from PDF are now updated

        // Phase 3: Perform web search if needed
        var webResult = await webSearchAnalysisService.SearchForArtifactsIfNeeded(
            paper,
            new BugListDiscoveryResult
            {
                Doi = paper.Doi,
                Title = paper.Title,
                ArtifactRepositories = combinedRepositories.ToList(),
            }, // Pass current repos for decision logic
            cancellationToken
        );
        logger.LogInformation(
            "Web search found {BugCount} bug lists and {RepoCount} repositories.",
            webResult.BugLists.Count,
            webResult.ArtifactRepositories.Count
        );

        // Combine all findings
        var allBugLists = combinedBugLists.Concat(webResult.BugLists).ToList();
        var allRepositories = combinedRepositories.Concat(webResult.ArtifactRepositories).ToList();

        // Phase 4: Post-process and validate results
        var finalResult = PostProcessResults(paper.Doi, paper.Title, allBugLists, allRepositories);

        LogFinalResults(paper, finalResult);
        return finalResult;
    }

    private BugListDiscoveryResult PostProcessResults(
        string doi,
        string title,
        List<BugListSource> bugLists,
        List<ArtifactRepository> repositories
    )
    {
        logger.LogDebug("Post-processing results for paper {PaperDoi}", doi);

        var uniqueBugLists = bugLists
            .GroupBy(bl => bl.Url.ToLowerInvariant())
            .Select(g => g.OrderByDescending(bl => bl.Confidence).First())
            .ToList();

        var uniqueRepositories = repositories
            .GroupBy(ar => ar.Url.ToLowerInvariant())
            .Select(g => g.OrderByDescending(ar => ar.Confidence).First())
            .ToList();

        var thresholds = thresholdOptions.Value;
        var highConfidenceThreshold = thresholds.HighConfidenceThreshold;
        var minBugListConfidence = highConfidenceThreshold * 0.5;
        var minRepositoryConfidence = highConfidenceThreshold * 0.4;

        uniqueBugLists.RemoveAll(bl => bl.Confidence < minBugListConfidence);
        uniqueRepositories.RemoveAll(ar => ar.Confidence < minRepositoryConfidence);

        return new BugListDiscoveryResult
        {
            Doi = doi,
            Title = title,
            BugLists = uniqueBugLists,
            ArtifactRepositories = uniqueRepositories,
            DiscoverySuccessful = true,
        };
    }

    private void LogFinalResults(Paper paper, BugListDiscoveryResult result)
    {
        logger.LogInformation(
            "Bug list discovery completed for paper {PaperDoi}. Found {BugListCount} bug lists and {RepoCount} artifact repositories.",
            paper.Doi,
            result.BugLists.Count,
            result.ArtifactRepositories.Count
        );
    }

    public BugListDiscoverySummary GetDiscoverySummary(List<BugListDiscoveryResult> results)
    {
        if (results.Count == 0)
        {
            return new BugListDiscoverySummary();
        }

        var papersWithBugLists = results.Count(r => r.BugLists.Count > 0);
        var papersWithArtifacts = results.Count(r => r.ArtifactRepositories.Count > 0);
        var papersWithHighConfidenceFindings = results.Count(r =>
            r.BugLists.Any(bl => bl.Confidence >= BugDiscoveryConstants.HighConfidenceThreshold)
            || r.ArtifactRepositories.Any(ar =>
                ar.Confidence >= BugDiscoveryConstants.HighConfidenceThreshold
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
}
