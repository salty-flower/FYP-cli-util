using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DataCollection.Services;

/// <summary>
/// Interface for web search services
/// </summary>
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

/// <summary>
/// Represents a web search result
/// </summary>
public class WebSearchResult
{
    /// <summary>
    /// Title of the search result
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// URL of the search result
    /// </summary>
    public required string Url { get; set; }

    /// <summary>
    /// Snippet or description of the search result
    /// </summary>
    public required string Snippet { get; set; }

    /// <summary>
    /// Source of the search (DuckDuckGo, Google, etc.)
    /// </summary>
    public required string Source { get; set; }
}
