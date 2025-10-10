using System.Diagnostics.CodeAnalysis;

namespace DataCollection.Infrastructure.Models.GitHub;

public record CachedHttpResponse(
    string Url,
    int StatusCode,
    Dictionary<string, string[]> Headers,
    string ResponseBody,
    string? ETag,
    DateTimeOffset CachedAt,
    DateTimeOffset? ExpiresAt
);

[SuppressMessage("Usage", "CA2227:Collection properties should be read only")]
public record CacheMetadata(
    string Url,
    int StatusCode,
    Dictionary<string, string[]> Headers,
    string? ETag,
    DateTimeOffset CachedAt,
    DateTimeOffset? ExpiresAt,
    long BodySizeBytes
);
