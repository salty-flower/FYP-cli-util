using GitHub.Models;

namespace DataCollection.Infrastructure.Clients.IssueTrackers;

public interface IRepositoryCache
{
    Task<FullRepository?> TryGetAsync(string owner, string repoName);
    Task SetAsync(string owner, string repoName, FullRepository repository);
}
