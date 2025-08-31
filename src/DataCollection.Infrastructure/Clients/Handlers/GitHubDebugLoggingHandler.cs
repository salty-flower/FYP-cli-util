using Microsoft.Extensions.Logging;

namespace DataCollection.Infrastructure.Clients.Handlers;

/// <summary>
/// HTTP message handler that logs detailed response information for 4xx errors from GitHub API
/// </summary>
public class GitHubDebugLoggingHandler(ILogger<GitHubDebugLoggingHandler> logger)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var response = await base.SendAsync(request, cancellationToken);

        // Log detailed information for 4xx errors
        if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
        {
            var requestHeaders = string.Join(
                "; ",
                request.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}")
            );

            var responseHeaders = string.Join(
                "; ",
                response.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}")
            );

            var contentHeaders =
                response.Content?.Headers != null
                    ? string.Join(
                        "; ",
                        response.Content.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}")
                    )
                    : string.Empty;

            logger.LogDebug(
                "GitHub API 4xx Error Details - "
                    + "URI: {RequestUri}, "
                    + "Method: {Method}, "
                    + "Status: {StatusCode}, "
                    + "Request Headers: {RequestHeaders}, "
                    + "Response Headers: {ResponseHeaders}, "
                    + "Content Headers: {ContentHeaders}",
                request.RequestUri,
                request.Method,
                response.StatusCode,
                requestHeaders,
                responseHeaders,
                contentHeaders
            );

            // Also log response content for debugging if available
            if (response.Content != null)
            {
                try
                {
                    var responseContent = await response.Content.ReadAsStringAsync(
                        cancellationToken
                    );
                    if (!string.IsNullOrEmpty(responseContent))
                    {
                        logger.LogDebug(
                            "GitHub API 4xx Error Response Body: {ResponseBody}",
                            responseContent
                        );
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug("Failed to read response content: {Exception}", ex.Message);
                }
            }
        }

        return response;
    }
}
