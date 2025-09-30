using System.Diagnostics.CodeAnalysis;
using DataCollection.Core.Models.IssueTracker;
using DataCollection.Core.Models.IssueTracker.Responses;
using DataCollection.Infrastructure.Extensions;
using DataCollection.Infrastructure.Persistence;
using DataCollection.Infrastructure.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataCollection.Application.Features.IssueProcessing;

public class DatabaseIssueAnalysisStorageService(
    ILogger<DatabaseIssueAnalysisStorageService> logger,
    IDbContextFactory<DataCollectionDbContext> dbContextFactory
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
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        try
        {
            var existingEntity = await dbContext.IssueAnalyses.FirstOrDefaultAsync(ia =>
                ia.Owner == owner && ia.Repository == repoName && ia.IssueNumber == issueNumber
            );

            if (existingEntity != null)
            {
                existingEntity.Status = status;
                existingEntity.AnalysisJson = System.Text.Json.JsonSerializer.Serialize(
                    analysisResult,
                    AppJsonContext.Default.IssueAnalysisResponse
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
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();

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
        if (result == null)
            logger.LogWarning(
                "Failed to deserialize cached analysis for {Owner}/{Repo}#{IssueNumber}",
                owner,
                repoName,
                issueNumber
            );

        return result;
    }
}
