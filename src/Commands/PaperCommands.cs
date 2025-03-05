using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConsoleAppFramework;
using DataCollection.Models;
using DataCollection.Options;
using DataCollection.Services;
using DataCollection.Utils;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Python.Runtime;

namespace DataCollection.Commands;

[RegisterCommands]
public class PaperCommands(
    AcmScraper scraper,
    ILogger<PaperCommands> logger,
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
    public async Task ScrapeAsync(
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
    public async Task DownloadAsync(CancellationToken cancellationToken = default)
    {
        var paperMetadataDir = new DirectoryInfo(_pathsOptions.PaperMetadataDir);
        var paperBinDir = new DirectoryInfo(_pathsOptions.PaperBinDir);

        if (!paperBinDir.Exists)
            paperBinDir.Create();

        logger.LogInformation("Loading papers from metadata...");
        var papers = new List<Paper>();

        foreach (var file in paperMetadataDir.GetFiles("*.bin"))
        {
            var bin = await File.ReadAllBytesAsync(file.FullName, cancellationToken);
            var paper = MemoryPackSerializer.Deserialize<Paper>(bin);
            if (paper != null)
            {
                papers.Add(paper);
            }
        }

        logger.LogInformation("Found {Count} papers to download", papers.Count);
        await scraper.DownloadPapersAsync(papers, paperBinDir.FullName, cancellationToken);
        logger.LogInformation("Downloads completed");
    }

    /// <summary>
    /// Analyze paper metadata for keywords
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public void AnalyzeMetadata(CancellationToken cancellationToken = default)
    {
        var paperMetadataDir = new DirectoryInfo(_pathsOptions.PaperMetadataDir);

        logger.LogInformation("Analyzing paper metadata...");

        var papers = paperMetadataDir
            .GetFiles("*.bin")
            .Select(async file =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bin = await File.ReadAllBytesAsync(file.FullName, cancellationToken);
                return (file.Name, MemoryPackSerializer.Deserialize<Paper>(bin));
            })
            .Select(t => t.Result)
            .Where(t => t.Item2 != null)
            .Select(t =>
            {
                if (t.Item2 == null)
                {
                    logger.LogWarning("Null paper {FileName}, skip", t.Name);
                    return null;
                }
                return t.Item2;
            })
            .Where(p => p != null)
            .Cast<Paper>();

        foreach (var paper in papers)
        {
            var keywordCounts = PaperAnalyzer.CountKeywordsInText(
                paper.Abstract,
                _keywordsOptions.MustExist
            );

            var messagePrefix = $"Processing {paper.Doi} - ";
            var messageSuffix = $" - {paper.Title}";

            if (!keywordCounts.Any(k => k.Value > 0))
            {
                logger.LogInformation(
                    "{prefix} No keywords found in abstract {suffix}",
                    messagePrefix,
                    messageSuffix
                );
            }
            else
            {
                logger.LogInformation(
                    "{prefix} - Keywords: {KeywordCounts} {suffix}",
                    messagePrefix,
                    string.Join(
                        ", ",
                        keywordCounts
                            .Where(k => k.Value > 0)
                            .Select(kvp => $"{kvp.Key}*{kvp.Value}")
                    ),
                    messageSuffix
                );
            }
        }

        logger.LogInformation("Metadata analysis completed");
    }

    /// <summary>
    /// Process and analyze PDF files
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task AnalyzePdfsAsync(CancellationToken cancellationToken = default)
    {
        var pdfDataDir = new DirectoryInfo(_pathsOptions.PdfDataDir);
        var paperBinDir = new DirectoryInfo(_pathsOptions.PaperBinDir);

        if (!pdfDataDir.Exists)
            pdfDataDir.Create();

        // Configure Python environment
        Runtime.PythonDLL = _pathsOptions.PythonDLL;
        PythonEngine.Initialize();
        PythonEngine.BeginAllowThreads();

        var filter = new Filter(paperBinDir);

        // Get already processed files
        var alreadyDumped = pdfDataDir.GetFiles("*.bin").Select(f => f.Name.Replace(".bin", ""));
        var alreadyDumpedDict = alreadyDumped.ToDictionary(p => p, _ => true).AsReadOnly();

        logger.LogInformation("Processing PDF files...");
        var processedCount = 0;

        await foreach (
            var pdfData in filter
                .FilterPapersAsync(alreadyDumpedDict)
                .WithCancellation(cancellationToken)
        )
        {
            processedCount++;
            logger.LogInformation("Processing {FileName}", pdfData.FileName);

            // Analyze keywords in PDF
            var keywordCounts = PaperAnalyzer.CountKeywordsInTexts(
                pdfData.Texts,
                _keywordsOptions.Analysis
            );

            logger.LogInformation(
                "Keywords: {KeywordCounts}",
                string.Join(", ", keywordCounts.Select(kvp => $"{kvp.Key}*{kvp.Value}"))
            );

            // Serialize and save the PDF data
            var bin = MemoryPackSerializer.Serialize<PdfData>(pdfData);
            var binPath = Path.Combine(pdfDataDir.FullName, $"{pdfData.FileName}.bin");
            await File.WriteAllBytesAsync(binPath, bin, cancellationToken);
        }

        logger.LogInformation("PDF analysis completed, processed {Count} PDFs", processedCount);
    }

    /// <summary>
    /// Run the entire pipeline: scrape, download, and analyze
    /// </summary>
    /// <param name="proceedingDOI">-p, The DOI of the proceedings to scrape</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task RunPipelineAsync(
        string proceedingDOI = "10.1145/3597503",
        CancellationToken cancellationToken = default
    )
    {
        logger.LogInformation("Starting full pipeline...");

        await ScrapeAsync(proceedingDOI, cancellationToken);
        await DownloadAsync(cancellationToken);
        AnalyzeMetadata(cancellationToken);
        await AnalyzePdfsAsync(cancellationToken);

        logger.LogInformation("Pipeline completed successfully");
    }
}
