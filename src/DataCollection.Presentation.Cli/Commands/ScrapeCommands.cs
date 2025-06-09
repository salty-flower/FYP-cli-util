using ConsoleAppFramework;
using DataCollection.Core.Models;
using DataCollection.Infrastructure.Clients;
using DataCollection.Infrastructure.Options;
using DataCollection.Infrastructure.Persistence;
using DataCollection.Presentation.Cli.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Presentation.Cli.Commands;

/// <summary>
/// Commands for scraping and downloading papers
/// </summary>
[RegisterCommands("scrape")]
[ConsoleAppFilter<PathsOptionsFilter>]
public class ScrapeCommands(
    AcmScraper scraper,
    AcmPaperDownloader paperDownloader,
    ILogger<ScrapeCommands> logger,
    IOptions<PathsOptions> pathsOptions,
    DatabaseDataLoadingService databaseDataLoadingService
)
{
    private readonly PathsOptions _pathsOptions = pathsOptions.Value;

    /// <summary>
    /// Scrape paper metadata from ACM
    /// </summary>
    /// <param name="proceedingDOI">-p, The DOI of the proceedings to scrape</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task Metadata(string proceedingDOI, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting paper metadata scraping...");
        var count = 0;

        await foreach (var paper in scraper.GetSectionPapersAsync(proceedingDOI, cancellationToken))
        {
            count++;
            logger.LogInformation("Processing paper: {Title}", paper.Title);
            await databaseDataLoadingService.SavePaperAsync(paper);
        }

        logger.LogInformation("Completed scraping {Count} papers", count);
    }

    /// <summary>
    /// Download PDF papers from scraped metadata
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task Download(CancellationToken cancellationToken = default)
    {
        var paperBinDir = new DirectoryInfo(_pathsOptions.PaperBinDir);

        logger.LogInformation("Loading papers from database...");
        var papers = await databaseDataLoadingService.LoadPapersAsync();

        logger.LogInformation("Found {Count} papers to download", papers.Count);
        await paperDownloader.DownloadPapersAsync(papers, paperBinDir.FullName, cancellationToken);
        logger.LogInformation("Downloads completed");
    }

    /// <summary>
    /// Run the entire pipeline: scrape, download, and dump
    /// </summary>
    /// <param name="proceedingDOI">-p, The DOI of the proceedings to scrape</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task Pipeline(string proceedingDOI, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting full pipeline...");

        await Metadata(proceedingDOI, cancellationToken);
        await Download(cancellationToken);

        // Use the analysis commands
        var dumpCmd = new DumpCommands(logger, pathsOptions, databaseDataLoadingService);
        await dumpCmd.PDF(cancellationToken);

        logger.LogInformation("Pipeline completed successfully");
    }
}
