using System;

namespace DataCollection.Infrastructure.Options;

public class HttpCacheOptions
{
    public bool Enabled { get; init; } = true;
    public TimeSpan DefaultTtl { get; init; } = TimeSpan.FromDays(30);
    public TimeSpan CommitsTtl { get; init; } = TimeSpan.FromDays(3650);
    public long MaxCacheSizeBytes { get; init; } = 1_000_000_000;
}
