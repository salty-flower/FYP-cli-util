using System.Collections.Concurrent;
using DataCollection.Infrastructure.Options;
using DataCollection.Infrastructure.Serialization;
using GitHub.Models;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

public class RepositoryCache(IOptions<PathsOptions> pathsOptions) : IRepositoryCache
{
    private readonly ConcurrentDictionary<string, FullRepository> repositoryCache = [];
    private readonly string issueRepoDir = pathsOptions.Value.IssueRepoDir;

    public async Task<FullRepository?> TryGetAsync(string owner, string repoName)
    {
        var cacheKey = $"{owner}/{repoName}";
        if (repositoryCache.TryGetValue(cacheKey, out var cachedRepo))
        {
            return cachedRepo;
        }

        var repoFile = GetRepositoryFilePath(owner, repoName);
        if (!File.Exists(repoFile))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(repoFile);
        var repo = JsonConvert.DeserializeObject<FullRepository>(json);
        if (repo?.Id == 0)
        {
            return null;
        }

        if (repo != null)
        {
            repositoryCache[cacheKey] = repo;
        }
        return repo;
    }

    public async Task SetAsync(string owner, string repoName, FullRepository repository)
    {
        var cacheKey = $"{owner}/{repoName}";
        repositoryCache[cacheKey] = repository;

        var repoDir = Path.Combine(issueRepoDir, owner);
        if (!Directory.Exists(repoDir))
        {
            Directory.CreateDirectory(repoDir);
        }

        var repoFile = GetRepositoryFilePath(owner, repoName);
        await File.WriteAllTextAsync(
            repoFile,
            JsonSerializer.Serialize(repository, GitHubAPIJsonContext.Default.FullRepository)
        );
    }

    private string GetRepositoryFilePath(string owner, string repoName) =>
        Path.Combine(issueRepoDir, owner, $"{repoName}.json");
}
