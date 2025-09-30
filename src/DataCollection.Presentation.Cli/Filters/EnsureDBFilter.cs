using ConsoleAppFramework;
using DataCollection.Infrastructure.Extensions;
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

        // scan for invalid cached analyses (e.g., due to schema changes)
        var invalidAnalyses = dbContext
            .IssueAnalyses.AsAsyncEnumerable()
            .Where(ia => ia.ToCachedAnalysisResult() is null);

        if (await invalidAnalyses.AnyAsync(cancellationToken))
        {
            logger.LogWarning(
                "Found {Count} invalid cached analyses. Removing them.",
                invalidAnalyses.CountAsync()
            );
            dbContext.IssueAnalyses.RemoveRange(
                invalidAnalyses.ToBlockingEnumerable(cancellationToken)
            );
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await Next.InvokeAsync(context, cancellationToken);
    }
}
