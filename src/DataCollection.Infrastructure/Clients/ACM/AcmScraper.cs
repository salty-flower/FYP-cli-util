using System.Runtime.CompilerServices;
using DataCollection.Core.Models;
using DataCollection.Infrastructure.Options;
using HtmlAgilityPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Infrastructure.Clients.ACM;

public class AcmScraper(
    IHttpClientFactory httpClientFactory,
    ILogger<AcmScraper> logger,
    IServiceProvider serviceProvider
)
{
    private const string LazySectionSelector =
        "//div[contains(@class, 'toc__section') and .//div[contains(@class, 'accordion-lazy')]]";

    public async IAsyncEnumerable<Paper> GetSectionPapersAsync(
        string proceedingDOI,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        using var httpClient = httpClientFactory.CreateClient("acm-scraper");
        var proceedingUrl = $"/doi/proceedings/{proceedingDOI}";

        logger.LogInformation("Fetching proceedings from {Url}", proceedingUrl);
        var rootHtml = await httpClient.GetStringAsync(proceedingUrl, cancellationToken);
        var rootDoc = new HtmlDocument();
        rootDoc.LoadHtml(rootHtml);

        var strategy = CreateStrategy(rootDoc.DocumentNode, proceedingDOI);

        if (strategy == null)
        {
            logger.LogWarning(
                "Could not determine a scraping strategy for DOI {DOI}",
                proceedingDOI
            );
            yield break;
        }

        await foreach (var paper in strategy.ScrapeAsync(rootDoc.DocumentNode, cancellationToken))
            yield return paper;
    }

    private IScrapingStrategy? CreateStrategy(HtmlNode rootNode, string proceedingDOI)
    {
        var lazySectionNodes = rootNode.SelectNodes(LazySectionSelector);
        if (lazySectionNodes is { Count: > 0 })
        {
            logger.LogDebug("Selected SectionedLayoutStrategy");
            return new SectionedLayoutStrategy(
                httpClientFactory,
                serviceProvider.GetRequiredService<IOptionsSnapshot<ParallelismOptions>>(),
                serviceProvider.GetRequiredService<ILogger<SectionedLayoutStrategy>>(),
                serviceProvider.GetRequiredService<AcmPaperParser>(),
                proceedingDOI
            );
        }

        var pbContextMeta = rootNode.SelectSingleNode("//meta[@name='pbContext']");
        var pbContext = pbContextMeta?.Attributes["content"].Value ?? string.Empty;

        if (string.IsNullOrEmpty(pbContext))
            return null;
        logger.LogDebug("Selected PaginatedLayoutStrategy");
        return new PaginatedLayoutStrategy(
            httpClientFactory,
            serviceProvider.GetRequiredService<ILogger<PaginatedLayoutStrategy>>(),
            serviceProvider.GetRequiredService<AcmPaperParser>(),
            pbContext
        );
    }
}
