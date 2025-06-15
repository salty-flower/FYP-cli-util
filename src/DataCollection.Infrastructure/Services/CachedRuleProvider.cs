using DataCollection.Core.Interfaces;
using DataCollection.Core.Models.Database;
using DataCollection.Infrastructure.Options;
using DataCollection.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DataCollection.Infrastructure.Services;

public class CachedRuleProvider : IRuleProvider
{
    private readonly IDbContextFactory<DataCollectionDbContext> _dbContextFactory;
    private readonly IMemoryCache _cache;
    private readonly PatternMatchingOptions _options;

    public CachedRuleProvider(
        IDbContextFactory<DataCollectionDbContext> dbContextFactory,
        IMemoryCache cache,
        IOptions<PatternMatchingOptions> options
    )
    {
        _dbContextFactory = dbContextFactory;
        _cache = cache;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<PatternRule>> GetPatternsAsync(
        string category,
        CancellationToken cancellationToken = default
    )
    {
        var cacheKey = $"patterns-{category}";
        return await _cache.GetOrCreateAsync(
            cacheKey,
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(
                    _options.CacheExpirationMinutes
                );
                await using var dbContext = await _dbContextFactory.CreateDbContextAsync(
                    cancellationToken
                );
                return await dbContext
                    .PatternRules.AsNoTracking()
                    .Where(p => p.Category == category && p.IsActive)
                    .OrderBy(p => p.Priority)
                    .ToListAsync(cancellationToken);
            }
        );
    }

    public async Task<IReadOnlyList<KeywordRule>> GetKeywordsAsync(
        string category,
        CancellationToken cancellationToken = default
    )
    {
        var cacheKey = $"keywords-{category}";
        return await _cache.GetOrCreateAsync(
            cacheKey,
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(
                    _options.CacheExpirationMinutes
                );
                await using var dbContext = await _dbContextFactory.CreateDbContextAsync(
                    cancellationToken
                );
                return await dbContext
                    .KeywordRules.AsNoTracking()
                    .Where(k => k.Category == category && k.IsActive)
                    .OrderBy(k => k.Priority)
                    .ToListAsync(cancellationToken);
            }
        );
    }

    public async Task<IReadOnlyList<UrlTypeRule>> GetUrlTypeRulesAsync(
        CancellationToken cancellationToken = default
    )
    {
        var cacheKey = "url-types";
        return await _cache.GetOrCreateAsync(
            cacheKey,
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(
                    _options.CacheExpirationMinutes
                );
                await using var dbContext = await _dbContextFactory.CreateDbContextAsync(
                    cancellationToken
                );
                return await dbContext
                    .UrlTypeRules.AsNoTracking()
                    .Where(u => u.IsActive)
                    .OrderByDescending(u => u.Priority)
                    .ToListAsync(cancellationToken);
            }
        );
    }
}
