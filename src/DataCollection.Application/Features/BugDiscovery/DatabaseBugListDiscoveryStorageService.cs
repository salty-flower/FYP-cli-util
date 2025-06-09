using System.Text.Json;
using DataCollection.Application.Models.Export.BugAnalysis;
using DataCollection.Core.Models.Database;
using DataCollection.Infrastructure.Options;
using DataCollection.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataCollection.Application.Features.BugDiscovery;

public class DatabaseBugListDiscoveryStorageService(
    ILogger<DatabaseBugListDiscoveryStorageService> logger,
    DataCollectionDbContext dbContext
)
{
    public async Task SaveBugListDiscoveryAsync(JobName jobName, BugListDiscoveryAnalysis analysis)
    {
        try
        {
            var existingEntity = await dbContext.BugListDiscoveries.FirstOrDefaultAsync(bd =>
                bd.Conf == jobName.Conf && bd.Year == jobName.Year
            );

            var analysisJson = JsonSerializer.Serialize(analysis);

            if (existingEntity != null)
            {
                existingEntity.AnalysisJson = analysisJson;
                existingEntity.UpdatedAt = DateTime.UtcNow;
                logger.LogDebug("Updated existing bug list discovery for {JobName}", jobName);
            }
            else
            {
                var entity = new BugListDiscoveryEntity
                {
                    Conf = jobName.Conf,
                    Year = jobName.Year,
                    AnalysisJson = analysisJson,
                };

                dbContext.BugListDiscoveries.Add(entity);
                logger.LogDebug("Created new bug list discovery for {JobName}", jobName);
            }

            await dbContext.SaveChangesAsync();
            logger.LogInformation("Saved bug list discovery analysis for {JobName}", jobName);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error saving bug list discovery for {JobName}: {Error}",
                jobName,
                ex.Message
            );
            throw;
        }
    }

    public async Task<BugListDiscoveryAnalysis?> GetBugListDiscoveryAsync(JobName jobName)
    {
        try
        {
            var entity = await dbContext.BugListDiscoveries.FirstOrDefaultAsync(bd =>
                bd.Conf == jobName.Conf && bd.Year == jobName.Year
            );

            if (entity == null)
            {
                logger.LogDebug("No bug list discovery found for {JobName}", jobName);
                return null;
            }

            var analysis = JsonSerializer.Deserialize<BugListDiscoveryAnalysis>(
                entity.AnalysisJson
            );
            logger.LogDebug("Retrieved bug list discovery for {JobName}", jobName);
            return analysis;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to retrieve bug list discovery for {JobName}", jobName);
            return null;
        }
    }
}
