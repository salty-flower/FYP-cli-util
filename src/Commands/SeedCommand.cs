using System;
using System.Collections.Generic;
using System.Text;
using ConsoleAppFramework;
using DataCollection.Services;
using Microsoft.EntityFrameworkCore;

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
