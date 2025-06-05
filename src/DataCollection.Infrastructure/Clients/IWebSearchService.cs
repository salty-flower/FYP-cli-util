using DataCollection.Infrastructure.Models.WebSearch;

namespace DataCollection.Infrastructure.Clients;

public interface IWebSearchService
{
    /// <summary>
    /// Perform a web search with the given query
    /// </summary>
    /// <param name="query">Search query</param>
    /// <param name="maxResults">Maximum number of results to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of search results</returns>
    Task<List<WebSearchResult>> SearchAsync(
        string query,
        int maxResults = 10,
        CancellationToken cancellationToken = default
    );
}
