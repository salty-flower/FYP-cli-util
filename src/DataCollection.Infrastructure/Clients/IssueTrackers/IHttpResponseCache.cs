using System.Net.Http;
using DataCollection.Infrastructure.Models.GitHub;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

public interface IHttpResponseCache
{
    Task<CachedHttpResponse?> GetAsync(HttpRequestMessage request, CancellationToken ct);
    Task SetAsync(HttpResponseMessage response, TimeSpan? ttl, CancellationToken ct);
    Task<bool> ExistsAsync(HttpRequestMessage request, CancellationToken ct);
    Task InvalidateAsync(HttpRequestMessage request, CancellationToken ct);
    Task<long> GetCacheSizeAsync(CancellationToken ct);
}
