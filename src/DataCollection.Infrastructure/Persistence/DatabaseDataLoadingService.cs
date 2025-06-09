using DataCollection.Core.Models;
using DataCollection.Infrastructure.Extensions;
using DataCollection.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Infrastructure.Persistence;

public class DatabaseDataLoadingService(
    ILogger<DatabaseDataLoadingService> logger,
    DataCollectionDbContext dbContext,
    IOptionsSnapshot<RootOptions> rootOptions,
    IOptionsSnapshot<PathsOptions> pathsOptions
)
{
    private readonly JobName jobName = (
        rootOptions.Value.TryParseJobName(out var jobName) ? jobName : jobName
    )!.Value;

    public async Task<List<PdfData>> LoadPdfDataAsync()
    {
        logger.LogInformation("Loading PDF data from database");

        var entities = await dbContext.PdfData.ToListAsync();
        var pdfDataList = entities.Select(e => e.ToPdfData()).ToList();

        logger.LogInformation("Loaded {Count} PDF documents", pdfDataList.Count);
        return pdfDataList;
    }

    public async Task<List<PdfData>> LoadAllPdfDataAsync()
    {
        logger.LogInformation("Loading all PDF data from database");

        var entities = await dbContext.PdfData.ToListAsync();
        var pdfDataList = entities.Select(e => e.ToPdfData()).ToList();

        logger.LogInformation("Loaded {Count} PDF documents from all jobs", pdfDataList.Count);
        return pdfDataList;
    }

    public async Task<PdfData?> LoadPdfDataAsync(string doi)
    {
        try
        {
            var sanitizedDoi = doi.Replace("/", "-");
            var entity = await dbContext
                .PdfData.Include(p => p.Paper)
                .FirstOrDefaultAsync(p =>
                    p.Paper != null && p.Paper.Doi.Replace("/", "-") == sanitizedDoi
                );

            if (entity == null)
            {
                logger.LogWarning("PDF data not found for DOI: {Doi}", doi);
                return null;
            }

            logger.LogDebug("Loaded PDF data for DOI: {Doi}", doi);
            return entity.ToPdfData();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error loading PDF data for DOI {Doi}: {Error}", doi, ex.Message);
            return null;
        }
    }

    public async Task<List<Paper>> LoadPapersAsync()
    {
        logger.LogInformation("Loading papers from database");

        var entities = await dbContext.Papers.ToListAsync();
        var papers = entities.Select(e => e.ToPaper()).ToList();

        logger.LogInformation("Loaded {Count} papers", papers.Count);
        return papers;
    }

    public async Task<PdfData?> LoadPdfDataByDoiAsync(string sanitizedDoi)
    {
        try
        {
            var entity = await dbContext
                .PdfData.Include(p => p.Paper)
                .FirstOrDefaultAsync(p =>
                    p.Paper != null && p.Paper.Doi.Replace("/", "-") == sanitizedDoi
                );

            if (entity == null)
            {
                logger.LogWarning("PDF data not found for DOI: {SanitizedDoi}", sanitizedDoi);
                return null;
            }

            logger.LogDebug("Loaded PDF data for DOI: {SanitizedDoi}", sanitizedDoi);
            return entity.ToPdfData();
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Error loading PDF data for DOI {SanitizedDoi}: {Error}",
                sanitizedDoi,
                ex.Message
            );
            return null;
        }
    }

    public async Task SavePaperAsync(Paper paper)
    {
        try
        {
            var existingPaper = await dbContext.Papers.FirstOrDefaultAsync(p =>
                p.Doi == paper.Doi && p.Conf == jobName.Conf && p.Year == jobName.Year
            );

            if (existingPaper != null)
            {
                logger.LogDebug("Paper already exists: {Doi}", paper.Doi);
                return;
            }

            var entity = paper.ToEntity(jobName);
            dbContext.Papers.Add(entity);
            await dbContext.SaveChangesAsync();

            logger.LogDebug("Saved paper: {Title}", paper.Title);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving paper {Doi}: {Error}", paper.Doi, ex.Message);
            throw;
        }
    }

    public async Task SavePdfDataAsync(PdfData pdfData, string? doi = null)
    {
        try
        {
            int? paperId = null;
            if (!string.IsNullOrEmpty(doi))
            {
                var paper = await dbContext.Papers.FirstOrDefaultAsync(p =>
                    p.Doi == doi && p.Conf == jobName.Conf && p.Year == jobName.Year
                );
                paperId = paper?.Id;
            }

            var existingPdfData = await dbContext.PdfData.FirstOrDefaultAsync(p =>
                p.FileName == pdfData.FileName && p.Conf == jobName.Conf && p.Year == jobName.Year
            );

            if (existingPdfData != null)
            {
                logger.LogDebug("PDF data already exists: {FileName}", pdfData.FileName);
                return;
            }

            var entity = pdfData.ToEntity(jobName, paperId);
            dbContext.PdfData.Add(entity);
            await dbContext.SaveChangesAsync();

            logger.LogDebug("Saved PDF data: {FileName}", pdfData.FileName);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error saving PDF data {FileName}: {Error}",
                pdfData.FileName,
                ex.Message
            );
            throw;
        }
    }
}
