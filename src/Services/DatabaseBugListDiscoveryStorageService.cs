using System.Text.Json;
using DataCollection.Models.Database;
using DataCollection.Models.Export.BugAnalysis;
using DataCollection.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataCollection.Services;

public class DatabaseBugListDiscoveryStorageService(
    ILogger<DatabaseBugListDiscoveryStorageService> logger,
    DataCollectionDbContext dbContext
)
{
    public async Task SaveBugListDiscoveryAsync(
        string conf,
        int year,
        BugListDiscoveryAnalysis analysis
    )
    {
        try
        {
            var existingEntity = await dbContext.BugListDiscoveries.FirstOrDefaultAsync(bd =>
                bd.Conf == conf && bd.Year == year
            );

            var analysisJson = JsonSerializer.Serialize(analysis);

            if (existingEntity != null)
            {
                existingEntity.AnalysisJson = analysisJson;
                existingEntity.UpdatedAt = DateTime.UtcNow;
                logger.LogDebug(
                    "Updated existing bug list discovery for {Conf}-{Year}",
                    conf,
                    year
                );
            }
            else
            {
                var entity = new BugListDiscoveryEntity
                {
                    Conf = conf,
                    Year = year,
                    AnalysisJson = analysisJson,
                };

                dbContext.BugListDiscoveries.Add(entity);
                logger.LogDebug("Created new bug list discovery for {Conf}-{Year}", conf, year);
            }

            await dbContext.SaveChangesAsync();
            logger.LogInformation(
                "Saved bug list discovery analysis for {Conf}-{Year}",
                conf,
                year
            );
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error saving bug list discovery for {Conf}-{Year}: {Error}",
                conf,
                year,
                ex.Message
            );
            throw;
        }
    }

    public async Task SaveBugListDiscoveryAsync(string jobName, BugListDiscoveryAnalysis analysis)
    {
        var (conf, year) = JobNameParser.ParseJobName(jobName);
        await SaveBugListDiscoveryAsync(conf, year, analysis);
    }

    public async Task<BugListDiscoveryAnalysis?> GetBugListDiscoveryAsync(string conf, int year)
    {
        try
        {
            var entity = await dbContext.BugListDiscoveries.FirstOrDefaultAsync(bd =>
                bd.Conf == conf && bd.Year == year
            );

            if (entity == null)
            {
                logger.LogDebug("No bug list discovery found for {Conf}-{Year}", conf, year);
                return null;
            }

            var analysis = JsonSerializer.Deserialize<BugListDiscoveryAnalysis>(
                entity.AnalysisJson
            );
            logger.LogDebug("Retrieved bug list discovery for {Conf}-{Year}", conf, year);
            return analysis;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to retrieve bug list discovery for {Conf}-{Year}",
                conf,
                year
            );
            return null;
        }
    }

    public async Task<BugListDiscoveryAnalysis?> GetBugListDiscoveryAsync(string jobName)
    {
        var (conf, year) = JobNameParser.ParseJobName(jobName);
        return await GetBugListDiscoveryAsync(conf, year);
    }

    public async Task<
        List<(string Conf, int Year, BugListDiscoveryAnalysis Analysis)>
    > GetAllBugListDiscoveriesAsync()
    {
        try
        {
            var entities = await dbContext
                .BugListDiscoveries.OrderBy(bd => bd.Conf)
                .ThenBy(bd => bd.Year)
                .ToListAsync();

            var results = new List<(string Conf, int Year, BugListDiscoveryAnalysis Analysis)>();

            foreach (var entity in entities)
            {
                try
                {
                    var analysis = JsonSerializer.Deserialize<BugListDiscoveryAnalysis>(
                        entity.AnalysisJson
                    );
                    if (analysis != null)
                    {
                        results.Add((entity.Conf, entity.Year, analysis));
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Failed to deserialize bug list discovery for {Conf}-{Year}",
                        entity.Conf,
                        entity.Year
                    );
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving all bug list discoveries: {Error}", ex.Message);
            throw;
        }
    }

    public async Task EnsureDatabaseCreatedAsync()
    {
        await dbContext.Database.EnsureCreatedAsync();
        logger.LogInformation("Database ensured for bug list discovery storage");
    }
}
