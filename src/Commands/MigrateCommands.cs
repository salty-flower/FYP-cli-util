using ConsoleAppFramework;
using DataCollection.Filters;
using DataCollection.Models;
using DataCollection.Models.Database;
using DataCollection.Options;
using DataCollection.Services;
using DataCollection.Utils;
using MemoryPack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Commands;

[RegisterCommands("migrate")]
[ConsoleAppFilter<PathsOptions.Filter>]
public class MigrateCommands(
    ILogger<MigrateCommands> logger,
    IOptions<PathsOptions> pathsOptions,
    DataCollectionDbContext dbContext,
    DataLoadingService dataLoadingService
)
{
    private readonly PathsOptions _pathsOptions = pathsOptions.Value;

    /// <summary>
    /// Migrate paper metadata from binary files to SQLite database (current job only)
    /// </summary>
    public async Task PaperMetadata(CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "This command is deprecated. Use 'migrate alljobs' to migrate all job directories."
        );
    }

    /// <summary>
    /// Migrate PDF data from binary files to SQLite database
    /// </summary>
    public async Task PdfData(CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "This command is deprecated. Use 'migrate alljobs' to migrate all job directories."
        );
    }

    /// <summary>
    /// Migrate both paper metadata and PDF data to SQLite database
    /// </summary>
    public async Task All(CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "This command is deprecated. Use 'migrate alljobs' to migrate all job directories."
        );
    }

    /// <summary>
    /// Migrate ALL job folders from the data directory to the shared SQLite database
    /// </summary>
    public async Task AllJobs(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting migration of all job folders to shared database...");

        await dbContext.Database.EnsureCreatedAsync();

        var dataDir = new DirectoryInfo(_pathsOptions.BaseDir);
        if (!dataDir.Exists)
        {
            logger.LogWarning("Data directory does not exist: {DataDir}", dataDir.FullName);
            return;
        }

        var jobDirs = dataDir
            .GetDirectories()
            .Where(d => JobNameParser.TryParseJobName(d.Name, out _, out _))
            .ToList();

        if (jobDirs.Count == 0)
        {
            logger.LogInformation("No valid job directories found in {DataDir}", dataDir.FullName);
            return;
        }

        logger.LogInformation("Found {Count} job directories to migrate", jobDirs.Count);

        foreach (var jobDir in jobDirs)
        {
            var (conf, year) = JobNameParser.ParseJobName(jobDir.Name);

            logger.LogInformation(
                "Migrating job: {JobName} (conf: {Conf}, year: {Year})",
                jobDir.Name,
                conf,
                year
            );

            await MigrateJobDirectory(jobDir, conf, year, cancellationToken);
        }

        logger.LogInformation("All job migrations completed successfully!");
    }

    private async Task MigrateJobDirectory(
        DirectoryInfo jobDir,
        string conf,
        int year,
        CancellationToken cancellationToken
    )
    {
        var paperMetadataDir = Path.Combine(jobDir.FullName, "paper-metadata");
        var pdfDataDir = Path.Combine(jobDir.FullName, "pdfdata");

        // Migrate papers
        if (Directory.Exists(paperMetadataDir))
        {
            var papers = dataLoadingService.LoadPapersFromMetadata(paperMetadataDir);
            logger.LogInformation("Found {Count} papers in {JobDir}", papers.Count, jobDir.Name);

            foreach (var paper in papers)
            {
                var existingPaper = await dbContext.Papers.FirstOrDefaultAsync(
                    p => p.Doi == paper.Doi && p.Conf == conf && p.Year == year,
                    cancellationToken
                );

                if (existingPaper != null)
                {
                    logger.LogDebug(
                        "Paper already exists: {Doi} ({Conf}-{Year})",
                        paper.Doi,
                        conf,
                        year
                    );
                    continue;
                }

                var entity = paper.ToEntity(conf, year);
                dbContext.Papers.Add(entity);
            }
        }

        // Migrate PDF data
        if (Directory.Exists(pdfDataDir))
        {
            var pdfDataList = dataLoadingService.LoadPdfDataFromDirectory(pdfDataDir);
            logger.LogInformation(
                "Found {Count} PDF data entries in {JobDir}",
                pdfDataList.Count,
                jobDir.Name
            );

            foreach (var pdfData in pdfDataList)
            {
                var existingPdfData = await dbContext.PdfData.FirstOrDefaultAsync(
                    p => p.FileName == pdfData.FileName && p.Conf == conf && p.Year == year,
                    cancellationToken
                );

                if (existingPdfData != null)
                {
                    logger.LogDebug(
                        "PDF data already exists: {FileName} ({Conf}-{Year})",
                        pdfData.FileName,
                        conf,
                        year
                    );
                    continue;
                }

                // Try to find associated paper
                int? paperId = null;
                var sanitizedFileName = Path.GetFileNameWithoutExtension(pdfData.FileName);
                var paper = await dbContext.Papers.FirstOrDefaultAsync(
                    p =>
                        p.Doi.Replace("/", "-") == sanitizedFileName
                        && p.Conf == conf
                        && p.Year == year,
                    cancellationToken
                );

                if (paper != null)
                {
                    paperId = paper.Id;
                }

                var entity = pdfData.ToEntity(conf, year, paperId);
                dbContext.PdfData.Add(entity);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Completed migration for {JobDir}", jobDir.Name);
    }
}
