namespace DataCollection.Infrastructure.Clients.IssueTrackers;

public interface ISearchResultsCache
{
    Task<int?> TryGetAsync(string searchQuery);
    Task SetAsync(string searchQuery, int count);
}
