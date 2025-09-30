using System.Net;
using Microsoft.Extensions.Logging;

namespace DataCollection.Infrastructure.Clients.Handlers;

/// <summary>
/// HTTP message handler that manually follows redirects while preserving Authorization headers
/// This is necessary because HttpClient strips auth headers on redirects by default for security
/// </summary>
public class GitHubRedirectHandler(ILogger<GitHubRedirectHandler>? logger = null)
    : DelegatingHandler
{
    private const int MaxRedirects = 5;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var response = await base.SendAsync(request, cancellationToken);
        var redirectCount = 0;

        while (
            (
                response.StatusCode == HttpStatusCode.MovedPermanently
                || response.StatusCode == HttpStatusCode.Found
                || response.StatusCode == HttpStatusCode.SeeOther
                || response.StatusCode == HttpStatusCode.TemporaryRedirect
                || response.StatusCode == HttpStatusCode.PermanentRedirect
            )
            && redirectCount < MaxRedirects
        )
        {
            redirectCount++;

            if (response.Headers.Location == null)
            {
                logger?.LogWarning(
                    "Redirect response {StatusCode} has no Location header",
                    response.StatusCode
                );
                return response;
            }

            var redirectUri = response.Headers.Location.IsAbsoluteUri
                ? response.Headers.Location
                : new Uri(request.RequestUri!, response.Headers.Location);

            logger?.LogDebug(
                "Following redirect {Count}/{Max} from {Original} to {Redirect}",
                redirectCount,
                MaxRedirects,
                request.RequestUri,
                redirectUri
            );

            // Create new request with same headers (including Authorization)
            var redirectRequest = new HttpRequestMessage(request.Method, redirectUri);

            // Copy all headers from original request
            foreach (var header in request.Headers)
            {
                redirectRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            // Copy content if present (for POST/PUT)
            if (request.Content != null)
            {
                redirectRequest.Content = request.Content;
            }

            response.Dispose();
            response = await base.SendAsync(redirectRequest, cancellationToken);
        }

        if (redirectCount >= MaxRedirects)
        {
            logger?.LogWarning(
                "Maximum redirect limit ({MaxRedirects}) reached for {RequestUri}",
                MaxRedirects,
                request.RequestUri
            );
        }

        return response;
    }
}
