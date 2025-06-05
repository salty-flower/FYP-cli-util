using DataCollection.Core.Models.IssueTracker;
using DataCollection.Core.Models.IssueTracker.Responses;
using DataCollection.Infrastructure.Extensions;
using DataCollection.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataCollection.Application.Features.IssueProcessing;

public class DatabaseIssueAnalysisStorageService(
    ILogger<DatabaseIssueAnalysisStorageService> logger,
    DataCollectionDbContext dbContext
)
{
    public async Task SaveAnalysisResultAsync(
        string owner,
        string repoName,
        long issueNumber,
        IssueStatus status,
        IssueAnalysisResponse analysisResult
    )
    {
        try
        {
            var existingEntity = await dbContext.IssueAnalyses.FirstOrDefaultAsync(ia =>
                ia.Owner == owner && ia.Repository == repoName && ia.IssueNumber == issueNumber
            );

            if (existingEntity != null)
            {
                existingEntity.Status = status;
                existingEntity.AnalysisJson = System.Text.Json.JsonSerializer.Serialize(
                    analysisResult
                );
                existingEntity.UpdatedAt = DateTime.UtcNow;
                logger.LogDebug(
                    "Updated existing analysis for {Owner}/{Repo}#{IssueNumber}",
                    owner,
                    repoName,
                    issueNumber
                );
            }
            else
            {
                var cachedResult = new CachedAnalysisResult
                {
                    Owner = owner,
                    Repository = repoName,
                    IssueNumber = issueNumber,
                    Status = status,
                    Analysis = analysisResult,
                };

                var entity = cachedResult.ToEntity();
                dbContext.IssueAnalyses.Add(entity);
                logger.LogDebug(
                    "Created new analysis for {Owner}/{Repo}#{IssueNumber}",
                    owner,
                    repoName,
                    issueNumber
                );
            }

            await dbContext.SaveChangesAsync();
            logger.LogInformation(
                "Saved analysis result for {Owner}/{Repo}#{IssueNumber}",
                owner,
                repoName,
                issueNumber
            );
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error saving analysis result for {Owner}/{Repo}#{IssueNumber}: {Error}",
                owner,
                repoName,
                issueNumber,
                ex.Message
            );
            throw;
        }
    }

    public async Task<CachedAnalysisResult?> TryGetCachedAnalysisResultAsync(
        string owner,
        string repoName,
        long issueNumber
    )
    {
        try
        {
            var entity = await dbContext.IssueAnalyses.FirstOrDefaultAsync(ia =>
                ia.Owner == owner && ia.Repository == repoName && ia.IssueNumber == issueNumber
            );

            if (entity == null)
            {
                logger.LogDebug(
                    "No cached analysis found for {Owner}/{Repo}#{IssueNumber}",
                    owner,
                    repoName,
                    issueNumber
                );
                return null;
            }

            var result = entity.ToCachedAnalysisResult();
            logger.LogDebug(
                "Retrieved cached analysis for {Owner}/{Repo}#{IssueNumber}",
                owner,
                repoName,
                issueNumber
            );
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to retrieve cached analysis for {Owner}/{Repo}#{IssueNumber}",
                owner,
                repoName,
                issueNumber
            );
            return null;
        }
    }

    public async Task<List<CachedAnalysisResult>> GetAnalysisResultsByRepositoryAsync(
        string owner,
        string repoName
    )
    {
        try
        {
            var entities = await dbContext
                .IssueAnalyses.Where(ia => ia.Owner == owner && ia.Repository == repoName)
                .OrderBy(ia => ia.IssueNumber)
                .ToListAsync();

            return entities.Select(e => e.ToCachedAnalysisResult()).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error retrieving analysis results for {Owner}/{Repo}: {Error}",
                owner,
                repoName,
                ex.Message
            );
            throw;
        }
    }

    public async Task<List<CachedAnalysisResult>> GetAnalysisResultsByStatusAsync(
        IssueStatus status
    )
    {
        try
        {
            var entities = await dbContext
                .IssueAnalyses.Where(ia => ia.Status == status)
                .OrderBy(ia => ia.Owner)
                .ThenBy(ia => ia.Repository)
                .ThenBy(ia => ia.IssueNumber)
                .ToListAsync();

            return entities.Select(e => e.ToCachedAnalysisResult()).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error retrieving analysis results by status {Status}: {Error}",
                status,
                ex.Message
            );
            throw;
        }
    }

    public async Task EnsureDatabaseCreatedAsync()
    {
        await dbContext.Database.EnsureCreatedAsync();
        logger.LogInformation("Database ensured for issue analysis storage");
    }
}
