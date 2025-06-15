using System.Text.Json;
using DataCollection.Core.Models.Database;
using DataCollection.Infrastructure.Models.BugList;
using DataCollection.Infrastructure.Options;
using DataCollection.Infrastructure.Persistence;
using DataCollection.Infrastructure.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataCollection.Infrastructure.Clients;

public class DatabaseBugListDiscoveryStorageService(
    ILogger<DatabaseBugListDiscoveryStorageService> logger,
    DataCollectionDbContext dbContext
)
{
    public async Task SaveBugListDiscoveryAsync(JobName jobName, BugListDiscoveryAnalysis analysis)
    {
        var allEntries = dbContext.BugListDiscoveries.AsAsyncEnumerable();
        var existingEntity = await allEntries.FirstOrDefaultAsync(bd =>
            bd.Conf == jobName.Conf && bd.Year == jobName.Year
        );

        var analysisJson = JsonSerializer.Serialize(
            analysis,
            AppJsonContext.Default.BugListDiscoveryAnalysis
        );

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
    }
}
