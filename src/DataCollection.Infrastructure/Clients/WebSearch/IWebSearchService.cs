using DataCollection.Infrastructure.Models.WebSearch;

namespace DataCollection.Infrastructure.Clients.WebSearch;

public interface IWebSearchService
{
    Task<List<WebSearchResult>> SearchAsync(
        string query,
        int maxResults = 10,
        CancellationToken cancellationToken = default
    );
}
