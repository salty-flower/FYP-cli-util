using ConsoleAppFramework;
using DataCollection.Application.Features.PatternMatching;
using DataCollection.Infrastructure.Persistence;

namespace DataCollection.Presentation.Cli.Commands;

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
