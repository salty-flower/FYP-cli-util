using Microsoft.Extensions.Logging;

namespace DataCollection.Common.Extensions;

public static class LoggingExtensions
{
    public static void LogOperationStart(this ILogger logger, string operation, int itemCount)
    {
        logger.LogInformation("Starting {Operation} for {ItemCount} items", operation, itemCount);
    }

    public static void LogOperationComplete(
        this ILogger logger,
        string operation,
        int processedCount,
        int successCount
    )
    {
        logger.LogInformation(
            "Completed {Operation}. Processed: {ProcessedCount}, Successful: {SuccessCount}",
            operation,
            processedCount,
            successCount
        );
    }

    public static void LogOperationProgress(
        this ILogger logger,
        string operation,
        int current,
        int total
    )
    {
        logger.LogDebug("Progress {Operation}: {Current}/{Total}", operation, current, total);
    }

    public static void LogValidationFailure(
        this ILogger logger,
        string parameter,
        string value,
        string reason
    )
    {
        logger.LogWarning(
            "Validation failed for {Parameter} = '{Value}': {Reason}",
            parameter,
            value,
            reason
        );
    }

    public static void LogCacheHit(this ILogger logger, string key, string entityType)
    {
        logger.LogDebug("Cache hit for {EntityType}: {Key}", entityType, key);
    }

    public static void LogCacheMiss(this ILogger logger, string key, string entityType)
    {
        logger.LogDebug("Cache miss for {EntityType}: {Key}", entityType, key);
    }
}
