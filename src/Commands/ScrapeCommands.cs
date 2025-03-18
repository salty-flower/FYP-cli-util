using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConsoleAppFramework;
using DataCollection.Models;
using DataCollection.Options;
using DataCollection.Services;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Commands;

/// <summary>
/// Commands for scraping and downloading papers
/// </summary>
[RegisterCommands("scrape")]
public class ScrapeCommands(
    AcmScraper scraper,
    ILogger<ScrapeCommands> logger,
    IOptions<PathsOptions> pathsOptions,
    IOptions<KeywordsOptions> keywordsOptions
)
{
    private readonly PathsOptions _pathsOptions = pathsOptions.Value;
    private readonly KeywordsOptions _keywordsOptions = keywordsOptions.Value;

    /// <summary>
    /// Scrape paper metadata from ACM
    /// </summary>
    /// <param name="proceedingDOI">-p, The DOI of the proceedings to scrape</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task Metadata(
        string proceedingDOI = "10.1145/3597503",
        CancellationToken cancellationToken = default
    )
    {
        var paperMetadataDir = new DirectoryInfo(_pathsOptions.PaperMetadataDir);
        if (!paperMetadataDir.Exists)
            paperMetadataDir.Create();

        logger.LogInformation("Starting paper metadata scraping...");
        var count = 0;

        await foreach (var paper in scraper.GetSectionPapersAsync(proceedingDOI, cancellationToken))
        {
            count++;
            logger.LogInformation("Processing paper: {Title}", paper.Title);
            var bin = MemoryPackSerializer.Serialize(paper);
            var binPath = Path.Combine(paperMetadataDir.FullName, $"{paper.SanitizedDoi}.bin");
            await File.WriteAllBytesAsync(binPath, bin, cancellationToken);
        }

        logger.LogInformation("Completed scraping {Count} papers", count);
    }

    /// <summary>
    /// Download PDF papers from scraped metadata
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task Download(CancellationToken cancellationToken = default)
    {
        var paperMetadataDir = new DirectoryInfo(_pathsOptions.PaperMetadataDir);
        var paperBinDir = new DirectoryInfo(_pathsOptions.PaperBinDir);

        if (!paperBinDir.Exists)
            paperBinDir.Create();

        logger.LogInformation("Loading papers from metadata...");
        var papers = await LoadPapersFromMetadataAsync(paperMetadataDir, cancellationToken);

        logger.LogInformation("Found {Count} papers to download", papers.Count);
        await scraper.DownloadPapersAsync(papers, paperBinDir.FullName, cancellationToken);
        logger.LogInformation("Downloads completed");
    }

    /// <summary>
    /// Run the entire pipeline: scrape, download, and dump
    /// </summary>
    /// <param name="proceedingDOI">-p, The DOI of the proceedings to scrape</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task Pipeline(
        string proceedingDOI = "10.1145/3597503",
        CancellationToken cancellationToken = default
    )
    {
        logger.LogInformation("Starting full pipeline...");

        await Metadata(proceedingDOI, cancellationToken);
        await Download(cancellationToken);

        // Use the analysis commands
        var dumpCmd = new DumpCommands(logger, pathsOptions);
        await dumpCmd.PDF(cancellationToken);

        logger.LogInformation("Pipeline completed successfully");
    }

    // Helper method to load papers from metadata directory
    private async Task<List<Paper>> LoadPapersFromMetadataAsync(
        DirectoryInfo metadataDir,
        CancellationToken cancellationToken = default
    )
    {
        var papers = new List<Paper>();

        foreach (var file in metadataDir.GetFiles("*.bin"))
        {
            if (cancellationToken.IsCancellationRequested)
                break;
            try
            {
                var bin = await File.ReadAllBytesAsync(file.FullName, cancellationToken);
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
