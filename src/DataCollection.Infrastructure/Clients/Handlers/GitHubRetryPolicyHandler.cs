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
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(ILogger? logger = null)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError() // Handle HttpRequestException and 5xx responses
            .OrResult(response => IsRateLimitResponse(response))
            .WaitAndRetryAsync(
                retryCount: 6, // Up to 6 retry attempts
                sleepDurationProvider: (retryAttempt) => CalculateRetryDelay(retryAttempt, logger),
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
    }

    private static bool IsRateLimitResponse(HttpResponseMessage response)
    {
        // GitHub returns 403 for secondary rate limits and 429 for primary rate limits
        return response.StatusCode == HttpStatusCode.Forbidden
            || response.StatusCode == HttpStatusCode.TooManyRequests;
    }

    private static TimeSpan CalculateRetryDelay(int retryAttempt, ILogger? logger)
    {
        // Use exponential backoff with jitter since we can't access response in this simple signature
        var baseDelay = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
        var totalDelay = baseDelay.Add(jitter);

        // Cap the delay at 5 minutes for GitHub secondary rate limits
        var maxDelay = TimeSpan.FromMinutes(5);
        var finalDelay = totalDelay > maxDelay ? maxDelay : totalDelay;

        logger?.LogInformation(
            "GitHub API retry attempt {RetryAttempt} - calculated delay: {Delay}s",
            retryAttempt,
            finalDelay.TotalSeconds
        );

        return finalDelay;
    }

    private static string? GetRateLimitRemaining(HttpResponseMessage? response)
    {
        return response
            ?.Headers.FirstOrDefault(h =>
                h.Key.Equals("X-RateLimit-Remaining", StringComparison.OrdinalIgnoreCase)
            )
            .Value?.FirstOrDefault();
    }

    private static DateTime? GetRateLimitReset(HttpResponseMessage? response)
    {
        var resetHeader = response
            ?.Headers.FirstOrDefault(h =>
                h.Key.Equals("X-RateLimit-Reset", StringComparison.OrdinalIgnoreCase)
            )
            .Value?.FirstOrDefault();

        if (resetHeader != null && long.TryParse(resetHeader, out var unixTimestamp))
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).UtcDateTime;
        }

        return null;
    }
}
