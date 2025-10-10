using DataCollection.Infrastructure.Models.GitHub;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

public interface IHttpResponseCache
{
    Task<CachedHttpResponse?> GetAsync(string url, CancellationToken ct);
    Task SetAsync(HttpResponseMessage response, TimeSpan? ttl, CancellationToken ct);
    Task<bool> ExistsAsync(string url, CancellationToken ct);
    Task InvalidateAsync(string url, CancellationToken ct);
    Task<long> GetCacheSizeAsync(CancellationToken ct);
}
