using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace DataCollection.Infrastructure.Clients.Handlers;

/// <summary>
/// Polly retry policy handler for Jira API rate limiting
/// Handles 429 responses with exponential backoff and jitter
/// Follows same pattern as GitHubRetryPolicyHandler
/// </summary>
public static class JiraRetryPolicyHandler
{
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(ILogger? logger = null)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError() // Handle HttpRequestException and 5xx responses
            .OrResult(response => IsRateLimitResponse(response))
            .WaitAndRetryAsync(
                retryCount: 5, // Up to 5 retry attempts for Jira
                sleepDurationProvider: (retryAttempt) => CalculateRetryDelay(retryAttempt, logger),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    var response = outcome.Result;
                    var statusCode = response?.StatusCode.ToString() ?? "Unknown";
                    var retryAfter = GetRetryAfterHeader(response);

                    logger?.LogWarning(
                        "Jira API retry attempt {RetryCount}/5 after {Delay}ms. "
                            + "Status: {StatusCode}, Retry-After: {RetryAfter}",
                        retryCount,
                        timespan.TotalMilliseconds,
                        statusCode,
                        retryAfter ?? "N/A"
                    );
                }
            );
    }

    private static bool IsRateLimitResponse(HttpResponseMessage response)
    {
        // Jira primarily returns 429 for rate limits
        // Some instances may return 403 for quota exceeded
        return response.StatusCode == HttpStatusCode.TooManyRequests
            || (
                response.StatusCode == HttpStatusCode.Forbidden
                && response.ReasonPhrase?.Contains("rate", StringComparison.OrdinalIgnoreCase)
                    == true
            );
    }

    private static TimeSpan CalculateRetryDelay(int retryAttempt, ILogger? logger)
    {
        // Jira Cloud rate limiting is dynamic, so use exponential backoff with jitter
        // as recommended by Atlassian documentation
        var baseDelay = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(100, 1000)); // 0.1-1s jitter
        var totalDelay = baseDelay.Add(jitter);

        // Cap the delay at 2 minutes for Jira (less aggressive than GitHub)
        var maxDelay = TimeSpan.FromMinutes(2);
        var finalDelay = totalDelay > maxDelay ? maxDelay : totalDelay;

        logger?.LogInformation(
            "Jira API retry attempt {RetryAttempt} - calculated delay: {Delay}s",
            retryAttempt,
            finalDelay.TotalSeconds
        );

        return finalDelay;
    }

    private static string? GetRetryAfterHeader(HttpResponseMessage? response)
    {
        // Jira may include Retry-After header in seconds or HTTP date format
        return response
            ?.Headers.FirstOrDefault(h =>
                h.Key.Equals("Retry-After", StringComparison.OrdinalIgnoreCase)
            )
            .Value?.FirstOrDefault();
    }
}
