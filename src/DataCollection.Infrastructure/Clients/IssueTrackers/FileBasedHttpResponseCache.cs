using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using DataCollection.Infrastructure.Models.GitHub;
using DataCollection.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

public class FileBasedHttpResponseCache : IHttpResponseCache
{
    private readonly string cacheRoot;
    private readonly ILogger<FileBasedHttpResponseCache> logger;
    private readonly HttpCacheOptions cacheOptions;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly ConcurrentDictionary<string, SemaphoreSlim> entryLocks = new();

    public FileBasedHttpResponseCache(
        IOptions<PathsOptions> pathsOptions,
        IOptions<HttpCacheOptions> cacheOptions,
        ILogger<FileBasedHttpResponseCache> logger
    )
    {
        this.logger = logger;
        this.cacheOptions = cacheOptions.Value;
        cacheRoot = pathsOptions.Value.HttpCacheDir;
        Directory.CreateDirectory(cacheRoot);
    }

    public async Task<CachedHttpResponse?> GetAsync(string url, CancellationToken ct)
    {
        if (!cacheOptions.Enabled)
        {
            return null;
        }

        var cacheKey = GetCacheKey(url);
        var cacheDir = GetCacheDirectory(cacheKey);
        var metaPath = GetMetadataPath(cacheDir);
        var bodyPath = GetBodyPath(cacheDir);

        if (!File.Exists(metaPath) || !File.Exists(bodyPath))
        {
            return null;
        }

        var cacheLock = GetLock(cacheKey);
        await cacheLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var metaStream = File.OpenRead(metaPath);
            var metadata = await JsonSerializer
                .DeserializeAsync<CacheMetadata>(metaStream, jsonOptions, ct)
                .ConfigureAwait(false);

            if (metadata is null)
            {
                return null;
            }

            var body = await File.ReadAllTextAsync(bodyPath, ct).ConfigureAwait(false);
            return new CachedHttpResponse(
                metadata.Url,
                metadata.StatusCode,
                metadata.Headers,
                body,
                metadata.ETag,
                metadata.CachedAt,
                metadata.ExpiresAt
            );
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            logger.LogWarning(ex, "Failed to read cached response for {Url}", url);
            TryDeleteDirectory(cacheDir);
            return null;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    public async Task SetAsync(HttpResponseMessage response, TimeSpan? ttl, CancellationToken ct)
    {
        if (!cacheOptions.Enabled || ttl is null)
        {
            return;
        }

        if (response.RequestMessage?.RequestUri is null)
        {
            return;
        }

        var url = response.RequestMessage.RequestUri.ToString();
        var cacheKey = GetCacheKey(url);
        var cacheDir = GetCacheDirectory(cacheKey);
        var metaPath = GetMetadataPath(cacheDir);
        var bodyPath = GetBodyPath(cacheDir);
        var cacheLock = GetLock(cacheKey);

        await cacheLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(cacheDir);
            var body = response.Content is not null
                ? await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)
                : string.Empty;

            var bodySize = Encoding.UTF8.GetByteCount(body);
            if (cacheOptions.MaxCacheSizeBytes > 0)
            {
                var currentSize = await GetCacheSizeAsync(ct).ConfigureAwait(false);
                if (currentSize + bodySize > cacheOptions.MaxCacheSizeBytes)
                {
                    logger.LogDebug(
                        "Skipping cache write for {Url} because it would exceed max size {MaxSizeBytes} bytes",
                        url,
                        cacheOptions.MaxCacheSizeBytes
                    );
                    return;
                }
            }

            await File.WriteAllTextAsync(bodyPath, body, ct).ConfigureAwait(false);

            var headers = CollectHeaders(response);
            var metadata = new CacheMetadata(
                url,
                (int)response.StatusCode,
                headers,
                response.Headers.ETag?.ToString(),
                DateTimeOffset.UtcNow,
                ttl.HasValue ? DateTimeOffset.UtcNow.Add(ttl.Value) : null,
                bodySize
            );

            await using var metaStream = File.Create(metaPath);
            await JsonSerializer
                .SerializeAsync(metaStream, metadata, jsonOptions, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to cache response for {Url}", url);
        }
        finally
        {
            cacheLock.Release();
        }
    }

    public Task<bool> ExistsAsync(string url, CancellationToken ct)
    {
        if (!cacheOptions.Enabled)
        {
            return Task.FromResult(false);
        }

        var cacheKey = GetCacheKey(url);
        var cacheDir = GetCacheDirectory(cacheKey);
        var exists = File.Exists(GetMetadataPath(cacheDir)) && File.Exists(GetBodyPath(cacheDir));
        return Task.FromResult(exists);
    }

    public async Task InvalidateAsync(string url, CancellationToken ct)
    {
        var cacheKey = GetCacheKey(url);
        var cacheDir = GetCacheDirectory(cacheKey);
        var cacheLock = GetLock(cacheKey);
        await cacheLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            TryDeleteDirectory(cacheDir);
        }
        finally
        {
            cacheLock.Release();
        }
    }

    public Task<long> GetCacheSizeAsync(CancellationToken ct)
    {
        if (!cacheOptions.Enabled)
        {
            return Task.FromResult(0L);
        }

        long totalSize = 0;
        if (!Directory.Exists(cacheRoot))
        {
            return Task.FromResult(totalSize);
        }

        foreach (var directory in Directory.EnumerateDirectories(cacheRoot))
        {
            ct.ThrowIfCancellationRequested();
            var metaPath = GetMetadataPath(directory);
            if (!File.Exists(metaPath))
            {
                continue;
            }

            try
            {
                using var stream = File.OpenRead(metaPath);
                var metadata = JsonSerializer.Deserialize<CacheMetadata>(stream, jsonOptions);
                if (metadata != null)
                {
                    totalSize += metadata.BodySizeBytes;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                logger.LogDebug(ex, "Failed to read metadata from {MetaPath}", metaPath);
            }
        }

        return Task.FromResult(totalSize);
    }

    private static Dictionary<string, string[]> CollectHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in response.Headers)
        {
            headers[header.Key] = header.Value.ToArray();
        }

        if (response.Content is not null)
        {
            foreach (var header in response.Content.Headers)
            {
                headers[header.Key] = header.Value.ToArray();
            }
        }

        return headers;
    }

    private static string GetCacheKey(string url)
    {
        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(url));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private string GetCacheDirectory(string cacheKey) => Path.Combine(cacheRoot, cacheKey);

    private static string GetMetadataPath(string cacheDir) => Path.Combine(cacheDir, "meta.json");

    private static string GetBodyPath(string cacheDir) => Path.Combine(cacheDir, "body");

    private SemaphoreSlim GetLock(string cacheKey) =>
        entryLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));

    private static void TryDeleteDirectory(string cacheDir)
    {
        try
        {
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Ignore transient IO issues on delete
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore permission issues
        }
    }
}
