using DataCollection.Core.Models;
using HtmlAgilityPack;

namespace DataCollection.Infrastructure.Clients.ACM;

public interface IScrapingStrategy
{
    IAsyncEnumerable<Paper> ScrapeAsync(HtmlNode rootNode, CancellationToken cancellationToken);
}
