using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Web;
using DataCollection.Core.Models;
using DataCollection.Infrastructure.Options;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Infrastructure.Clients.ACM;

public class SectionedLayoutStrategy(
    IHttpClientFactory httpClientFactory,
    IOptionsSnapshot<ParallelismOptions> parallelOpt,
    ILogger<SectionedLayoutStrategy> logger,
    AcmPaperParser acmPaperParser,
    string proceedingDOI
) : IScrapingStrategy
{
    private const string TocSectionSelector =
        "//div[contains(@class, 'toc__section') and .//div[contains(@class, 'accordion-lazy')]]";
    private const string TocWrapperSelector =
        ".//div[contains(@class, 'table-of-content-wrapper')]";
    private const string AccordionLazySelector = ".//div[contains(@class, 'accordion-lazy')]";
    private const string SectionTitleSelector = ".//a[contains(@class, 'section__title')]";

    public async IAsyncEnumerable<Paper> ScrapeAsync(
        HtmlNode rootNode,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var sectionNodes = rootNode.SelectNodes(TocSectionSelector);
        var tocWrapperNode = rootNode.SelectSingleNode(TocWrapperSelector);
        var rootDataWidgetId = tocWrapperNode?.Attributes["data-widgetid"]?.Value ?? string.Empty;

        if (sectionNodes == null || sectionNodes.Count == 0)
        {
            logger.LogInformation("No section nodes found for SectionedLayoutStrategy.");
            yield break;
        }

        logger.LogInformation(
            "Found {Count} lazy-loaded sections to process (Sectioned Layout) using Channels",
            sectionNodes.Count
        );

        var sectionChannel = Channel.CreateBounded<(string headingId, string doi)>(
            new BoundedChannelOptions(parallelOpt.Value.SectionProcessing)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = true,
            }
        );

        var resultsChannel = Channel.CreateUnbounded<Paper>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }
        );

        var producerTask = Task.Run(
            async () =>
            {
                try
                {
                    foreach (var sectionNode in sectionNodes)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var lazyLoadDiv = sectionNode.SelectSingleNode(AccordionLazySelector);
                        var sectionDoi = lazyLoadDiv?.Attributes["data-doi"].Value;
                        var sectionTitleLink = sectionNode.SelectSingleNode(SectionTitleSelector);
                        var sectionHeadingId = sectionTitleLink?.Attributes["id"].Value;

                        if (
                            string.IsNullOrEmpty(sectionDoi)
                            || string.IsNullOrEmpty(sectionHeadingId)
                            || string.IsNullOrEmpty(rootDataWidgetId)
                        )
                        {
                            logger.LogWarning(
                                "Skipping section in producer due to missing attributes (DOI: {SectionDoi}, HeadingID: {SectionHeadingId}, RootWidgetId: {RootWidgetId})",
                                sectionDoi,
                                sectionHeadingId,
                                rootDataWidgetId
                            );
                            continue;
                        }

                        await sectionChannel.Writer.WriteAsync(
                            (sectionHeadingId, sectionDoi),
                            cancellationToken
                        );
                    }
                }
                catch (OperationCanceledException)
                {
                    logger.LogDebug("Section producer task canceled.");
                }
                finally
                {
                    sectionChannel.Writer.TryComplete();
                }
            },
            cancellationToken
        );

        var consumerTasks = new List<Task>();
        for (int i = 0; i < parallelOpt.Value.SectionProcessing; i++)
        {
            consumerTasks.Add(
                Task.Run(
                    async () =>
                    {
                        try
                        {
                            await foreach (
                                var (headingId, doi) in sectionChannel.Reader.ReadAllAsync(
                                    cancellationToken
                                )
                            )
                            {
                                try
                                {
                                    logger.LogDebug(
                                        "Processing section {SectionId} with DOI {SectionDoi}",
                                        headingId,
                                        doi
                                    );

                                    var sectionHtml = await GetSectionAsync(
                                        headingId,
                                        doi,
                                        rootDataWidgetId,
                                        cancellationToken
                                    );
                                    var sectionContentDoc = new HtmlDocument();
                                    sectionContentDoc.LoadHtml(sectionHtml);

                                    foreach (
                                        var paper in acmPaperParser.ParsePapersFromNode(
                                            sectionContentDoc.DocumentNode
                                        )
                                    )
                                    {
                                        cancellationToken.ThrowIfCancellationRequested();
                                        await resultsChannel.Writer.WriteAsync(
                                            paper,
                                            cancellationToken
                                        );
                                    }
                                }
                                catch (OperationCanceledException)
                                {
                                    logger.LogTrace(
                                        "Section consumer sub-task canceled for section {SectionId}",
                                        headingId
                                    );
                                    throw;
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            logger.LogDebug("Section consumer task canceled.");
                        }
                    },
                    cancellationToken
                )
            );
        }

        _ = Task.Run(
            async () =>
            {
                await producerTask;
                await Task.WhenAll(consumerTasks);
                resultsChannel.Writer.TryComplete();
            },
            cancellationToken
        );

        await foreach (var paper in resultsChannel.Reader.ReadAllAsync(cancellationToken))
            yield return paper;

        logger.LogInformation("Finished processing sectioned layout using Channels.");
    }

    private async Task<string> GetSectionAsync(
        string sectionTocHeading,
        string sectionDoi,
        string rootDataWidgetId,
        CancellationToken cancellationToken = default
    )
    {
        using var httpClient = httpClientFactory.CreateClient("acm-scraper");
        var sectionUrl =
            $"/pb/widgets/lazyLoadTOC?tocHeading={sectionTocHeading}&widgetId={rootDataWidgetId}&doi={HttpUtility.UrlEncode(sectionDoi)}&pbContext=%3Btaxonomy%3Ataxonomy%3Aconference-collections%3Bissue%3Aissue%3Adoi%5C%3A{HttpUtility.UrlEncode(proceedingDOI)}%3Bwgroup%3Astring%3AACM%20Publication%20Websites%3BgroupTopic%3Atopic%3Aacm-pubtype%3Eproceeding%3Bcsubtype%3Astring%3AConference%20Proceedings%3Bpage%3Astring%3ABook%20Page%3Bwebsite%3Awebsite%3Adl-site%3Bctype%3Astring%3ABook%20Content%3Btopic%3Atopic%3Aconference-collections%3Eicse%3Barticle%3Aarticle%3Adoi%5C%3A{HttpUtility.UrlEncode(proceedingDOI)}%3Bjournal%3Ajournal%3Aacmconferences%3BpageGroup%3Astring%3APublication%20Pages";
        var sectionHtml = await httpClient.GetStringAsync(sectionUrl, cancellationToken);
        return sectionHtml;
    }
}
