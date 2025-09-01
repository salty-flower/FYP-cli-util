using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace DataCollection.Infrastructure.Clients.Handlers;

/// <summary>
/// Polly retry policy handler specifically for Bugzilla API interactions
/// Implements exponential backoff with jitter, similar to GitHub and Jira handlers
/// </summary>
public static class BugzillaRetryPolicyHandler
{
    /// <summary>
    /// Gets the retry policy for Bugzilla API calls
    /// </summary>
    /// <param name="logger">Optional logger for retry information</param>
    /// <returns>Configured retry policy</returns>
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(ILogger? logger = null)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(response => IsRateLimitResponse(response))
            .WaitAndRetryAsync(
                retryCount: 5,
                sleepDurationProvider: (retryAttempt) => CalculateRetryDelay(retryAttempt, logger),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    var responseCode = outcome.Result?.StatusCode.ToString() ?? "Exception";
                    logger?.LogWarning(
                        "Bugzilla API retry {RetryCount}/5 after {Delay}ms due to {StatusCode}",
                        retryCount,
                        timespan.TotalMilliseconds,
                        responseCode
                    );
                }
            );
    }

    /// <summary>
    /// Determines if a response indicates rate limiting
    /// </summary>
    private static bool IsRateLimitResponse(HttpResponseMessage response)
    {
        // Bugzilla typically uses 429 for rate limiting
        // Some instances may also use 503 Service Unavailable during high load
        return response.StatusCode == HttpStatusCode.TooManyRequests
            || response.StatusCode == HttpStatusCode.ServiceUnavailable;
    }

    /// <summary>
    /// Calculates retry delay with exponential backoff and jitter
    /// </summary>
    private static TimeSpan CalculateRetryDelay(int retryAttempt, ILogger? logger = null)
    {
        // Exponential backoff: 2^retry * base delay (1 second)
        var exponentialDelay = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));

        // Add jitter to prevent thundering herd (±25% random variation)
        var jitter = Random.Shared.NextDouble() * 0.5 - 0.25; // -0.25 to +0.25
        var jitterDelay = TimeSpan.FromMilliseconds(
            exponentialDelay.TotalMilliseconds * (1 + jitter)
        );

        // Cap maximum delay at 30 seconds
        var finalDelay = jitterDelay.TotalSeconds > 30 ? TimeSpan.FromSeconds(30) : jitterDelay;

        logger?.LogDebug(
            "Bugzilla retry delay calculated: attempt {Attempt}, base {BaseMs}ms, with jitter {FinalMs}ms",
            retryAttempt,
            exponentialDelay.TotalMilliseconds,
            finalDelay.TotalMilliseconds
        );

        return finalDelay;
    }
}
