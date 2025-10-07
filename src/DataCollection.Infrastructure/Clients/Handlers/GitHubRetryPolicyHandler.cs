using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace DataCollection.Infrastructure.Clients.Handlers;

/// <summary>
/// Polly retry policy handler specifically for GitHub API rate limiting
/// Handles both 403 (secondary rate limits) and 429 (primary rate limits) with exponential backoff
/// </summary>
public static class GitHubRetryPolicyHandler
{
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(ILogger? logger = null) =>
        HttpPolicyExtensions
            .HandleTransientHttpError() // Handle HttpRequestException and 5xx responses
            .OrResult(response => IsRateLimitResponse(response))
            .WaitAndRetryAsync(
                retryCount: 6, // Up to 6 retry attempts
                sleepDurationProvider: CalculateRetryDelay,
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    var response = outcome.Result;
                    var statusCode = response?.StatusCode.ToString() ?? "Unknown";
                    var rateLimitRemaining = GetRateLimitRemaining(response);
                    var rateLimitReset = GetRateLimitReset(response);

                    logger?.LogWarning(
                        "GitHub API retry attempt {RetryCount}/6 after {Delay}ms. "
                            + "Status: {StatusCode}, RateLimit Remaining: {Remaining}, Reset: {Reset}",
                        retryCount,
                        timespan.TotalMilliseconds,
                        statusCode,
                        rateLimitRemaining ?? "N/A",
                        rateLimitReset?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "N/A"
                    );
                }
            );

    private static bool IsRateLimitResponse(HttpResponseMessage response) =>
        // GitHub returns 403 for secondary rate limits and 429 for primary rate limits
        response.StatusCode == HttpStatusCode.Forbidden
        || response.StatusCode == HttpStatusCode.TooManyRequests;

    private static TimeSpan CalculateRetryDelay(
        int retryAttempt,
        DelegateResult<HttpResponseMessage> outcome,
        Context context
    )
    {
        var response = outcome.Result;

        if (response is not null)
        {
            var rateLimitReset = GetRateLimitReset(response);

            if (rateLimitReset is DateTime resetUtc)
            {
                var now = DateTime.UtcNow;
                var delayUntilReset = resetUtc - now;

                if (delayUntilReset > TimeSpan.Zero)
                {
                    // Add a small buffer to ensure we're beyond the reset window
                    return delayUntilReset.Add(TimeSpan.FromSeconds(1));
                }
            }
        }

        return CalculateFallbackDelay(retryAttempt);
    }

    private static TimeSpan CalculateFallbackDelay(int retryAttempt)
    {
        // Use exponential backoff with jitter as a fallback when reset information isn't available
        var baseDelay = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
        var totalDelay = baseDelay.Add(jitter);

        // Cap the delay at 5 minutes for GitHub secondary rate limits
        var maxDelay = TimeSpan.FromMinutes(5);
        var finalDelay = totalDelay > maxDelay ? maxDelay : totalDelay;

        return finalDelay;
    }

    private static string? GetRateLimitRemaining(HttpResponseMessage? response) =>
        response
            ?.Headers.FirstOrDefault(h =>
                h.Key.Equals("X-RateLimit-Remaining", StringComparison.OrdinalIgnoreCase)
            )
            .Value?.FirstOrDefault();

    private static DateTime? GetRateLimitReset(HttpResponseMessage? response)
    {
        var resetHeader = response
            ?.Headers.FirstOrDefault(h =>
                h.Key.Equals("X-RateLimit-Reset", StringComparison.OrdinalIgnoreCase)
            )
            .Value?.FirstOrDefault();

        return resetHeader != null && long.TryParse(resetHeader, out var unixTimestamp)
            ? DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).UtcDateTime
            : null;
    }
}
