using ConsoleAppFramework;
using DataCollection.Services;

namespace DataCollection.Commands;

[RegisterCommands("seed")]
internal class SeedCommand(
    DataCollectionDbContext dbContext,
    IPatternConfigurationService patternConfiguration
)
{
    public async Task Seed()
    {
        await dbContext.Database.EnsureCreatedAsync();
        await patternConfiguration.SeedDefaultPatternsAsync();
    }
}
