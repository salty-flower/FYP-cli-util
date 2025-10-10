using System.Diagnostics.CodeAnalysis;

namespace DataCollection.Infrastructure.Models.GitHub;

public record CachedHttpResponse(
    string Url,
    int StatusCode,
    CachedResponseHeaders Headers,
    string ResponseBody,
    string? ETag,
    DateTimeOffset CachedAt,
    DateTimeOffset? ExpiresAt
);

[SuppressMessage("Usage", "CA2227:Collection properties should be read only")]
public record CachedResponseHeaders(
    Dictionary<string, string[]> Response,
    Dictionary<string, string[]> Content
);

[SuppressMessage("Usage", "CA2227:Collection properties should be read only")]
public record CacheMetadata(
    string Url,
    int StatusCode,
    CachedResponseHeaders Headers,
    string? ETag,
    DateTimeOffset CachedAt,
    DateTimeOffset? ExpiresAt,
    long BodySizeBytes
);
