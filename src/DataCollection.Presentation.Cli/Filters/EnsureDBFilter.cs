using ConsoleAppFramework;
using DataCollection.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace DataCollection.Presentation.Cli.Filters;

internal class EnsureDBFilter(
    ConsoleAppFilter next,
    ILogger<EnsureDBFilter> logger,
    DataCollectionDbContext dbContext
) : ConsoleAppFilter(next)
{
    public override async Task InvokeAsync(
        ConsoleAppContext context,
        CancellationToken cancellationToken
    )
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        logger.LogInformation("Database created");
        await Next.InvokeAsync(context, cancellationToken);
    }
}
