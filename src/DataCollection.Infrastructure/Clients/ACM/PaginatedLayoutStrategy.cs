using System.Runtime.CompilerServices;
using System.Web;
using DataCollection.Core.Models;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace DataCollection.Infrastructure.Clients.ACM;

public class PaginatedLayoutStrategy(
    IHttpClientFactory httpClientFactory,
    ILogger<PaginatedLayoutStrategy> logger,
    AcmPaperParser acmPaperParser,
    string pbContext
) : IScrapingStrategy
{
    private const string SeeMoreSelector = ".//div[contains(@class, 'see_more')]";
    private const string ShowMoreButtonSelector =
        ".//button[contains(@class, 'showMoreProceedings')]";

    public async IAsyncEnumerable<Paper> ScrapeAsync(
        HtmlNode rootNode,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        logger.LogInformation("Processing as Non-Sectioned Layout (Paginated See More)");

        // 1. Parse papers initially visible on the page
        logger.LogInformation("Parsing initially visible papers...");
        var initialPaperCount = 0;
        foreach (var paper in acmPaperParser.ParsePapersFromNode(rootNode))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return paper;
            initialPaperCount++;
        }
        logger.LogInformation("Parsed {Count} initial papers.", initialPaperCount);

        // 2. Process 'See More' sequence
        var firstSeeMoreDiv = rootNode.SelectSingleNode(SeeMoreSelector);
        var firstShowMoreButton = firstSeeMoreDiv?.SelectSingleNode(ShowMoreButtonSelector);

        var currentDataUrl = firstShowMoreButton?.Attributes["data-url"].Value;
        var currentDataDoi = firstShowMoreButton?.Attributes["data-doi"].Value;
        var currentDataId = firstShowMoreButton?.Attributes["data-id"].Value;
        var currentWidgetId = rootNode.Attributes["data-widgetid"].Value;

        if (
            string.IsNullOrEmpty(currentDataUrl)
            || string.IsNullOrEmpty(currentDataDoi)
            || string.IsNullOrEmpty(currentDataId)
            || string.IsNullOrEmpty(currentWidgetId)
            || string.IsNullOrEmpty(pbContext)
        )
        {
            logger.LogWarning(
                "Could not initiate 'See More' sequence: missing initial data attributes or pbContext."
            );
            yield break;
        }

        using var httpClient = httpClientFactory.CreateClient("acm-scraper");
        while (!string.IsNullOrEmpty(currentDataId))
        {
            logger.LogInformation("Fetching 'See More' chunk with ID: {DataId}", currentDataId);
            string? nextDataId = null;

            var seeMoreUrl =
                $"{currentDataUrl}?id={currentDataId}&doi={HttpUtility.UrlEncode(currentDataDoi)}&widgetId={currentWidgetId}&pbContext={HttpUtility.UrlEncode(pbContext)}";
            logger.LogDebug("Requesting 'See More' URL: {SeeMoreUrl}", seeMoreUrl);

            var responseHtml = await httpClient.GetStringAsync(seeMoreUrl, cancellationToken);

            if (string.IsNullOrEmpty(responseHtml))
                logger.LogWarning(
                    "Received empty response for 'See More' chunk ID {DataId}",
                    currentDataId
                );
            else
            {
                var chunkDoc = new HtmlDocument();
                chunkDoc.LoadHtml(responseHtml);

                var chunkPaperCount = 0;
                foreach (var paper in acmPaperParser.ParsePapersFromNode(chunkDoc.DocumentNode))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return paper;
                    chunkPaperCount++;
                }
                logger.LogDebug(
                    "Parsed {Count} papers from chunk ID {DataId}",
                    chunkPaperCount,
                    currentDataId
                );
                var nextSeeMoreDiv = chunkDoc.DocumentNode.SelectSingleNode(
                    "//div[contains(@class, 'see_more')]"
                );
                var nextShowMoreButton = nextSeeMoreDiv?.SelectSingleNode(ShowMoreButtonSelector);
                nextDataId = nextShowMoreButton?.Attributes["data-id"].Value;
            }

            if (string.IsNullOrEmpty(nextDataId))
                logger.LogInformation(
                    "No further 'See More' button found in response for ID {DataId}. Reached end.",
                    currentDataId
                );

            currentDataId = nextDataId;
        }

        logger.LogInformation("Finished processing 'See More' sequence.");
    }
}
