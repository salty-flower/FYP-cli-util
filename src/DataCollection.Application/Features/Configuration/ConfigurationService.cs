using System.Text.Json;
using DataCollection.Core.Models.Database;
using DataCollection.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataCollection.Application.Features.Configuration;

public class ConfigurationService(
    DataCollectionDbContext dbContext,
    ILogger<ConfigurationService> logger
) : IConfigurationService
{
    private readonly Dictionary<string, object> _cache = new();
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public async Task<T?> GetValueAsync<T>(string category, string key, T? defaultValue = default)
    {
        await _cacheLock.WaitAsync();
        try
        {
            var cacheKey = $"{category}:{key}";
            if (_cache.TryGetValue(cacheKey, out var cachedValue))
            {
                return (T?)cachedValue;
            }

            var config = await dbContext.ConfigurationRules.FirstOrDefaultAsync(c =>
                c.Category == category && c.Key == key && c.IsActive
            );

            if (config == null)
            {
                _cache[cacheKey] = defaultValue;
                return defaultValue;
            }

            var value = DeserializeValue<T>(config.Value, config.DataType);
            _cache[cacheKey] = value;
            return value;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<List<T>> GetArrayAsync<T>(string category, string key)
    {
        var value = await GetValueAsync<List<T>>(category, key, new List<T>());
        return value ?? new List<T>();
    }

    public async Task<Dictionary<string, T>> GetCategoryAsync<T>(string category)
    {
        var configs = await dbContext
            .ConfigurationRules.Where(c => c.Category == category && c.IsActive)
            .ToListAsync();

        var result = new Dictionary<string, T>();
        foreach (var config in configs)
        {
            try
            {
                var value = DeserializeValue<T>(config.Value, config.DataType);
                if (value != null)
                {
                    result[config.Key] = value;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to deserialize configuration {Category}:{Key}",
                    category,
                    config.Key
                );
            }
        }

        return result;
    }

    public async Task SetValueAsync<T>(
        string category,
        string key,
        T value,
        string? description = null
    )
    {
        var (serializedValue, dataType) = SerializeValue(value);

        var existing = await dbContext.ConfigurationRules.FirstOrDefaultAsync(c =>
            c.Category == category && c.Key == key
        );

        if (existing != null)
        {
            existing.Value = serializedValue;
            existing.DataType = dataType;
            existing.Description = description ?? existing.Description;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            dbContext.ConfigurationRules.Add(
                new ConfigurationRule
                {
                    Category = category,
                    Key = key,
                    Value = serializedValue,
                    DataType = dataType,
                    Description = description ?? "",
                    IsActive = true,
                }
            );
        }

        await dbContext.SaveChangesAsync();

        // Update cache
        await _cacheLock.WaitAsync();
        try
        {
            var cacheKey = $"{category}:{key}";
            _cache[cacheKey] = value;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task SetArrayAsync<T>(
        string category,
        string key,
        IEnumerable<T> values,
        string? description = null
    )
    {
        await SetValueAsync(category, key, values.ToList(), description);
    }

    public async Task<bool> ExistsAsync(string category, string key)
    {
        return await dbContext.ConfigurationRules.AnyAsync(c =>
            c.Category == category && c.Key == key && c.IsActive
        );
    }

    public async Task RemoveAsync(string category, string key)
    {
        var config = await dbContext.ConfigurationRules.FirstOrDefaultAsync(c =>
            c.Category == category && c.Key == key
        );

        if (config != null)
        {
            config.IsActive = false;
            config.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();

            // Remove from cache
            await _cacheLock.WaitAsync();
            try
            {
                var cacheKey = $"{category}:{key}";
                _cache.Remove(cacheKey);
            }
            finally
            {
                _cacheLock.Release();
            }
        }
    }

    public async Task SeedDefaultsAsync()
    {
        if (await dbContext.ConfigurationRules.AnyAsync())
        {
            logger.LogInformation("Configuration rules already exist, skipping seeding");
            return;
        }

        logger.LogInformation("Seeding default configuration values...");

        var defaults = GetDefaultConfigurations();

        foreach (var (category, configs) in defaults)
        {
            foreach (var (key, value, description) in configs)
            {
                if (!await ExistsAsync(category, key))
                {
                    await SetValueAsync(category, key, value, description);
                }
            }
        }

        logger.LogInformation("Configuration seeding completed");
    }

    private static T? DeserializeValue<T>(string value, string dataType)
    {
        if (string.IsNullOrEmpty(value))
            return default;

        try
        {
            return dataType.ToLowerInvariant() switch
            {
                "string" => (T?)(object)value,
                "number" when typeof(T) == typeof(int) || typeof(T) == typeof(int?) => (T?)
                    (object)int.Parse(value),
                "number" when typeof(T) == typeof(double) || typeof(T) == typeof(double?) => (T?)
                    (object)double.Parse(value),
                "boolean" => (T?)(object)bool.Parse(value),
                "array" => JsonSerializer.Deserialize<T>(value),
                _ => JsonSerializer.Deserialize<T>(value),
            };
        }
        catch
        {
            return default;
        }
    }

    private static (string serializedValue, string dataType) SerializeValue<T>(T value)
    {
        if (value == null)
            return ("", "string");

        var type = typeof(T);

        if (type == typeof(string))
            return (value.ToString() ?? "", "string");

        if (
            type == typeof(int)
            || type == typeof(double)
            || type == typeof(float)
            || type == typeof(decimal)
        )
            return (value.ToString() ?? "0", "number");

        if (type == typeof(bool))
            return (value.ToString() ?? "false", "boolean");

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            return (JsonSerializer.Serialize(value), "array");

        return (JsonSerializer.Serialize(value), "object");
    }

    private static Dictionary<
        string,
        List<(string key, object value, string description)>
    > GetDefaultConfigurations()
    {
        return new Dictionary<string, List<(string, object, string)>>
        {
            [ConfigurationCategories.Thresholds] =
            [
                (
                    ConfigurationKeys.HighConfidenceThreshold,
                    0.8,
                    "High confidence threshold for pattern matching"
                ),
                (
                    ConfigurationKeys.MediumConfidenceThreshold,
                    0.6,
                    "Medium confidence threshold for pattern matching"
                ),
                (
                    ConfigurationKeys.LowConfidenceThreshold,
                    0.4,
                    "Low confidence threshold for pattern matching"
                ),
                (
                    ConfigurationKeys.MinimumConfidenceThreshold,
                    0.2,
                    "Minimum confidence threshold for pattern matching"
                ),
                (
                    ConfigurationKeys.DefaultMinimumConfidence,
                    0.5,
                    "Default minimum confidence for searches"
                ),
                (
                    ConfigurationKeys.QualityFilterMinConfidence,
                    0.7,
                    "Quality filter minimum confidence threshold"
                ),
            ],

            [ConfigurationCategories.Limits] =
            [
                (
                    ConfigurationKeys.MaxRepositoriesPerSearch,
                    50,
                    "Maximum repositories to process per search"
                ),
                (
                    ConfigurationKeys.MaxIssuesPerRepository,
                    1000,
                    "Maximum issues to analyze per repository"
                ),
                (
                    ConfigurationKeys.MaxFilesPerRepository,
                    500,
                    "Maximum files to analyze per repository"
                ),
                (ConfigurationKeys.MaxSearchResults, 100, "Maximum search results to return"),
            ],

            [ConfigurationCategories.FileNames] =
            [
                (
                    ConfigurationKeys.ReadmeFileNames,
                    new List<string>
                    {
                        "readme.md",
                        "readme.txt",
                        "readme.rst",
                        "readme",
                        "read.me",
                        "README.md",
                        "README.txt",
                        "README.rst",
                        "README",
                        "READ.ME",
                        "Readme.md",
                        "Readme.txt",
                        "Readme.rst",
                    },
                    "Common README file name variations"
                ),
                (
                    ConfigurationKeys.BugRelatedPaths,
                    new List<string>
                    {
                        "bug",
                        "issue",
                        "error",
                        "defect",
                        "fault",
                        "failure",
                        "test",
                        "spec",
                        "example",
                        "demo",
                        "sample",
                    },
                    "Path keywords that indicate bug-related content"
                ),
            ],

            [ConfigurationCategories.Keywords] =
            [
                (
                    ConfigurationKeys.ArtifactKeywords,
                    new List<string>
                    {
                        "artifact",
                        "benchmark",
                        "dataset",
                        "data",
                        "evaluation",
                        "experiment",
                        "implementation",
                        "tool",
                        "prototype",
                        "framework",
                        "library",
                        "system",
                        "model",
                        "algorithm",
                        "source code",
                        "code",
                        "repository",
                        "github",
                        "replication",
                        "reproduction",
                        "validation",
                        "verification",
                        "testing",
                        "case study",
                        "empirical study",
                        "user study",
                        "survey",
                        "analysis",
                        "measurement",
                        "metric",
                        "performance",
                        "comparison",
                        "baseline",
                        "ground truth",
                        "gold standard",
                        "reference",
                        "standard",
                        "specification",
                        "documentation",
                        "manual",
                        "guide",
                        "tutorial",
                        "example",
                        "demo",
                        "sample",
                        "template",
                        "pattern",
                        "best practice",
                        "methodology",
                        "approach",
                        "technique",
                        "strategy",
                        "solution",
                        "method",
                    },
                    "Keywords that indicate research artifacts"
                ),
                (
                    ConfigurationKeys.BugListKeywords,
                    new List<string>
                    {
                        "bug list",
                        "bug database",
                        "defect list",
                        "issue list",
                        "fault list",
                        "error list",
                        "failure list",
                        "problem list",
                        "anomaly list",
                        "inconsistency list",
                        "vulnerability list",
                        "security issue",
                        "known issues",
                        "reported bugs",
                    },
                    "Keywords that indicate bug lists or databases"
                ),
            ],

            [ConfigurationCategories.KnownHosts] =
            [
                (
                    ConfigurationKeys.RepositoryHosts,
                    new List<string>
                    {
                        "github.com",
                        "gitlab.com",
                        "bitbucket.org",
                        "sourceforge.net",
                        "codeplex.com",
                        "gitee.com",
                        "gitea.io",
                    },
                    "Known source code repository hosting services"
                ),
            ],

            [ConfigurationCategories.StopWords] =
            [
                (
                    ConfigurationKeys.SearchStopWords,
                    new List<string>
                    {
                        "the",
                        "and",
                        "or",
                        "but",
                        "in",
                        "on",
                        "at",
                        "to",
                        "for",
                        "of",
                        "with",
                        "by",
                        "from",
                        "as",
                        "is",
                        "was",
                        "are",
                        "were",
                        "be",
                        "been",
                        "being",
                    },
                    "Common stop words to exclude from searches"
                ),
                (
                    ConfigurationKeys.TitleStopWords,
                    new List<string>
                    {
                        "a",
                        "an",
                        "the",
                        "and",
                        "or",
                        "but",
                        "in",
                        "on",
                        "at",
                        "to",
                        "for",
                        "of",
                        "with",
                        "by",
                        "from",
                        "as",
                        "is",
                        "was",
                        "are",
                        "were",
                        "be",
                        "been",
                        "being",
                        "have",
                        "has",
                        "had",
                        "do",
                        "does",
                        "did",
                        "will",
                        "would",
                        "could",
                        "should",
                        "may",
                        "might",
                        "can",
                        "must",
                    },
                    "Stop words to exclude when extracting significant words from titles"
                ),
            ],

            [ConfigurationCategories.ArtifactSections] =
            [
                (
                    ConfigurationKeys.ArtifactSectionIdentifiers,
                    new List<string>
                    {
                        "artifact",
                        "data",
                        "code",
                        "implementation",
                        "tool",
                        "benchmark",
                        "dataset",
                        "evaluation",
                        "experiment",
                        "reproduction",
                        "replication",
                    },
                    "Section identifiers that indicate artifact availability"
                ),
            ],
        };
    }
}
