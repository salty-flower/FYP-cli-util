using System.Collections.Concurrent;
using System.Text.Json;
using DataCollection.Application.Models.IssueTracker.Profiles;
using DataCollection.Application.Serialization;
using DataCollection.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Application.Features.BugDiscovery.Caching;

public class UserProfileCache(IOptions<PathsOptions> pathsOptions, ILogger<UserProfileCache> logger)
    : IUserProfileCache
{
    private readonly ConcurrentDictionary<
        (string UserLogin, long RepositoryId),
        UserProfile
    > _memoryCache = new();
    private readonly string _diskCacheDirectory = Path.Combine(
        pathsOptions.Value.IssueProfileDir,
        "users"
    );

    public async Task<UserProfile?> GetAsync(string userLogin, long repositoryId)
    {
        // Check memory cache first
        if (_memoryCache.TryGetValue((userLogin, repositoryId), out var cachedProfile))
        {
            return cachedProfile;
        }

        // Check disk cache
        var userFile = GetCacheFilePath(userLogin, repositoryId);
        if (!File.Exists(userFile))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(userFile);
            var profile = JsonSerializer.Deserialize(
                json,
                ApplicationJsonContext.Default.UserProfile
            );
            if (profile != null)
            {
                _memoryCache.TryAdd((userLogin, repositoryId), profile);
                return profile;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to read cached user profile for {Login} in repo {RepoId}, deleting file.",
                userLogin,
                repositoryId
            );
            File.Delete(userFile);
        }

        return null;
    }

    public async Task SetAsync(string userLogin, long repositoryId, UserProfile userProfile)
    {
        _memoryCache[(userLogin, repositoryId)] = userProfile;

        var userFile = GetCacheFilePath(userLogin, repositoryId);
        try
        {
            var json = JsonSerializer.Serialize(
                userProfile,
                ApplicationJsonContext.Default.UserProfile
            );
            await File.WriteAllTextAsync(userFile, json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to write user profile to disk for {Login} in repo {RepoId}",
                userLogin,
                repositoryId
            );
        }
    }

    private string GetCacheFilePath(string userLogin, long repositoryId)
    {
        var repoUserDir = Path.Combine(_diskCacheDirectory, repositoryId.ToString());
        if (!Directory.Exists(repoUserDir))
        {
            Directory.CreateDirectory(repoUserDir);
        }
        return Path.Combine(repoUserDir, $"{userLogin}.json");
    }
}
