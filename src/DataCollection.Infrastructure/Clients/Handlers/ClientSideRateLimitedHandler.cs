using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;

namespace DataCollection.Infrastructure.Clients.Handlers;

public class ClientSideRateLimitedHandler(RateLimiter limiter)
    : DelegatingHandler(
        new SocketsHttpHandler
        {
            AllowAutoRedirect = false, // Prevent auth header stripping on redirects
        }
    )
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        using RateLimitLease lease = await limiter.AcquireAsync(permitCount: 1, cancellationToken);

        if (lease.IsAcquired)
            return await base.SendAsync(request, cancellationToken);

        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
            response.Headers.Add(
                "Retry-After",
                ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo)
            );

        return response;
    }
}
