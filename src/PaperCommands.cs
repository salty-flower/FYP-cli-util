using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConsoleAppFramework;
using DataCollection.Models;
using DataCollection.Services;
using MemoryPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Python.Runtime;

namespace DataCollection;

[RegisterCommands]
class PaperCommands(
    AcmScraper scraper,
    IConfiguration configuration,
    ILogger<PaperCommands> logger,
    PaperAnalyzer analyzer
)
{
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
        var paperMetadataDirPath = configuration.GetValue<string>("Paths:PaperMetadataDir");
        var paperMetadataDir = new DirectoryInfo(paperMetadataDirPath);
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
        var paperMetadataDirPath = configuration.GetValue<string>("Paths:PaperMetadataDir");
        var paperBinDirPath = configuration.GetValue<string>("Paths:PaperBinDir");

        var paperMetadataDir = new DirectoryInfo(paperMetadataDirPath);
        var paperBinDir = new DirectoryInfo(paperBinDirPath);

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
        await scraper.DownloadPapersAsync(papers, paperBinDirPath, cancellationToken);
        logger.LogInformation("Downloads completed");
    }

    /// <summary>
    /// Analyze paper metadata for keywords
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task AnalyzeMetadataAsync(CancellationToken cancellationToken = default)
    {
        var paperMetadataDirPath = configuration.GetValue<string>("Paths:PaperMetadataDir");
        var keywordsConfig = configuration.GetSection("Keywords");

        var paperMetadataDir = new DirectoryInfo(paperMetadataDirPath);
        var mustExistKeywords =
            keywordsConfig.GetSection("MustExist").Get<string[]>()
            ?? new[] { "bug", "test", "confirm", "develop", "detect" };

        logger.LogInformation("Analyzing paper metadata...");
        var validDois = new List<string>();

        foreach (var file in paperMetadataDir.GetFiles("*.bin"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bin = await File.ReadAllBytesAsync(file.FullName, cancellationToken);
            var paper = MemoryPackSerializer.Deserialize<Paper>(bin);

            if (paper == null)
            {
                logger.LogWarning("Null paper {FileName}, skip", file.Name);
                continue;
            }

            // Analyze and print keyword statistics
            var keywordCounts = analyzer.CountKeywordsInText(paper.Abstract, mustExistKeywords);
            string infoString = "";

            if (keywordCounts.Values.Sum() > 0)
            {
                validDois.Add(paper.SanitizedDoi);
                infoString =
                    string.Join(", ", keywordCounts.Select(kvp => $"{kvp.Key}*{kvp.Value}")) + "\n";
            }
            else
            {
                infoString = "No keywords found\n";
            }
            logger.LogInformation(
                "Processing {title} {doi}\n{info}",
                paper.Title,
                paper.SanitizedDoi,
                infoString
            );
        }

        logger.LogInformation("Metadata analysis completed");
    }

    /// <summary>
    /// Process and analyze PDF files
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task AnalyzePdfsAsync(CancellationToken cancellationToken = default)
    {
        var pdfDataDirPath = configuration.GetValue<string>("Paths:PdfDataDir");
        var paperBinDirPath = configuration.GetValue<string>("Paths:PaperBinDir");
        var pythonDllPath = configuration.GetValue<string>("Paths:PythonDLL");
        var keywordsConfig = configuration.GetSection("Keywords");

        var pdfDataDir = new DirectoryInfo(pdfDataDirPath);
        if (!pdfDataDir.Exists)
            pdfDataDir.Create();

        // Configure Python environment
        Runtime.PythonDLL = pythonDllPath;
        PythonEngine.Initialize();
        PythonEngine.BeginAllowThreads();

        var filter = new Filter(new DirectoryInfo(paperBinDirPath));

        // Get already processed files
        var alreadyDumped = pdfDataDir.GetFiles("*.bin").Select(f => f.Name.Replace(".bin", ""));
        var alreadyDumpedDict = alreadyDumped.ToDictionary(p => p, _ => true).AsReadOnly();

        var analysisKeywords =
            keywordsConfig.GetSection("Analysis").Get<string[]>()
            ?? new[] { "bug", "develop", "acknowledge", "maintain", "confirm" };

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
            var keywordCounts = analyzer.CountKeywordsInTexts(pdfData.Texts, analysisKeywords);

            logger.LogInformation(
                "Keywords: {KeywordCounts}",
                string.Join(", ", keywordCounts.Select(kvp => $"{kvp.Key}*{kvp.Value}"))
            );

            // Serialize and save the PDF data
            var bin = MemoryPackSerializer.Serialize(pdfData);
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
        await AnalyzeMetadataAsync(cancellationToken);
        await AnalyzePdfsAsync(cancellationToken);

        logger.LogInformation("Pipeline completed successfully");
    }
}
