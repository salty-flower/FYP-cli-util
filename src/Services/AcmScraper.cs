using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Web;
using DataCollection.Models;
using DataCollection.Options;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Services;

public class AcmScraper(
    IHttpClientFactory httpClientFactory,
    IOptionsSnapshot<ParallelismOptions> parallelOpt,
    ILogger<AcmScraper> logger,
    AcmPaperParser acmPaperParser
)
{
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

        var pbContextMeta = rootDoc.DocumentNode.SelectSingleNode("//meta[@name='pbContext']");
        var pbContext = pbContextMeta?.Attributes["content"]?.Value ?? string.Empty;

        var tocWrapperNode = rootDoc.DocumentNode.SelectSingleNode(
            ".//div[contains(@class, 'table-of-content-wrapper')]"
        );

        if (tocWrapperNode == null)
        {
            logger.LogWarning(
                "Could not find table-of-content-wrapper node for DOI {DOI}",
                proceedingDOI
            );
            yield break;
        }

        var rootDataWidgetId = tocWrapperNode.Attributes["data-widgetid"]?.Value ?? string.Empty; // Handle missing widget ID
        var lazySectionNodes = tocWrapperNode.SelectNodes(
            "//div[contains(@class, 'toc__section') and .//div[contains(@class, 'accordion-lazy')]]"
        );

        var asyncEnumerable =
            (lazySectionNodes != null && lazySectionNodes.Count > 0)
                ? ProcessSectionedLayoutAsync(
                    lazySectionNodes,
                    rootDataWidgetId,
                    proceedingDOI,
                    cancellationToken
                )
                : ProcessNonSectionedLayoutAsync(tocWrapperNode, pbContext, cancellationToken);

        await foreach (var paper in asyncEnumerable.WithCancellation(cancellationToken))
            yield return paper;
    }

    private async Task<string> GetSectionAsync(
        string sectionTocHeading,
        string sectionDoi,
        string rootDataWidgetId,
        string proceedingDOI,
        CancellationToken cancellationToken = default
    )
    {
        using var httpClient = httpClientFactory.CreateClient("acm-scraper");
        var sectionUrl =
            $"/pb/widgets/lazyLoadTOC?tocHeading={sectionTocHeading}&widgetId={rootDataWidgetId}&doi={HttpUtility.UrlEncode(sectionDoi)}&pbContext=%3Btaxonomy%3Ataxonomy%3Aconference-collections%3Bissue%3Aissue%3Adoi%5C%3A{HttpUtility.UrlEncode(proceedingDOI)}%3Bwgroup%3Astring%3AACM%20Publication%20Websites%3BgroupTopic%3Atopic%3Aacm-pubtype%3Eproceeding%3Bcsubtype%3Astring%3AConference%20Proceedings%3Bpage%3Astring%3ABook%20Page%3Bwebsite%3Awebsite%3Adl-site%3Bctype%3Astring%3ABook%20Content%3Btopic%3Atopic%3Aconference-collections%3Eicse%3Barticle%3Aarticle%3Adoi%5C%3A{HttpUtility.UrlEncode(proceedingDOI)}%3Bjournal%3Ajournal%3Aacmconferences%3BpageGroup%3Astring%3APublication%20Pages";
        var sectionHtml = await httpClient.GetStringAsync(sectionUrl, cancellationToken);
        return sectionHtml;
    }

    private async IAsyncEnumerable<Paper> ProcessSectionedLayoutAsync(
        HtmlNodeCollection sectionNodes,
        string rootDataWidgetId,
        string proceedingDOI,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        logger.LogInformation(
            "Found {Count} lazy-loaded sections to process (Sectioned Layout) using Channels",
            sectionNodes.Count
        );

        // Channel to pass section data (headingId, doi) to consumers
        var sectionChannel = Channel.CreateBounded<(string headingId, string doi)>(
            new BoundedChannelOptions(parallelOpt.Value.SectionProcessing) // Bound capacity for backpressure
            {
                FullMode = BoundedChannelFullMode.Wait, // Wait if channel is full
                SingleReader = false,
                SingleWriter = true, // Only one producer task
            }
        );

        // Channel to aggregate Paper results from consumers
        var resultsChannel = Channel.CreateUnbounded<Paper>(
            new UnboundedChannelOptions
            {
                SingleReader = true, // Only the main method reads results
                SingleWriter = false, // Multiple consumers write results
            }
        );

        // --- Producer Task ---
        var producerTask = Task.Run(
            async () =>
            {
                try
                {
                    foreach (var sectionNode in sectionNodes)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var lazyLoadDiv = sectionNode.SelectSingleNode(
                            ".//div[contains(@class, 'accordion-lazy')]"
                        );
                        var sectionDoi = lazyLoadDiv?.Attributes["data-doi"]?.Value;
                        var sectionTitleLink = sectionNode.SelectSingleNode(
                            ".//a[contains(@class, 'section__title')]"
                        );
                        var sectionHeadingId = sectionTitleLink?.Attributes["id"]?.Value;

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
                            continue; // Skip this section
                        }

                        // Write valid section data to the channel
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
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error in section producer task");
                    // Signal error to the writer - this will fault the channel reader
                    sectionChannel.Writer.TryComplete(ex);
                }
                finally
                {
                    // Mark the channel as complete when done producing
                    sectionChannel.Writer.TryComplete();
                }
            },
            cancellationToken
        );

        // --- Consumer Tasks ---
        var consumerTasks = new List<Task>();
        for (int i = 0; i < parallelOpt.Value.SectionProcessing; i++)
        {
            consumerTasks.Add(
                Task.Run(
                    async () =>
                    {
                        try
                        {
                            // Read from the section channel until it's complete
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
                                        proceedingDOI,
                                        cancellationToken
                                    );
                                    var sectionContentDoc = new HtmlDocument();
                                    sectionContentDoc.LoadHtml(sectionHtml);

                                    // Parse papers and write to results channel
                                    await foreach (
                                        var paper in acmPaperParser
                                            .ParsePapersFromNodeAsync(
                                                sectionContentDoc.DocumentNode,
                                                cancellationToken
                                            )
                                            .WithCancellation(cancellationToken)
                                    )
                                    {
                                        await resultsChannel.Writer.WriteAsync(
                                            paper,
                                            cancellationToken
                                        );
                                    }
                                }
                                catch (OperationCanceledException)
                                {
                                    // Expected if cancellation is requested during processing

                                    logger.LogTrace(
                                        "Section consumer sub-task canceled for section {SectionId}",
                                        headingId
                                    );
                                    throw; // Re-throw to cancel the foreach loop
                                }
                                catch (Exception ex)
                                {
                                    // Log error for specific section but continue processing others
                                    logger.LogError(
                                        ex,
                                        "Error processing section {SectionId} with DOI {Doi}",
                                        headingId,
                                        doi
                                    );
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            logger.LogDebug("Section consumer task canceled.");
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Error in section consumer task");
                            // Signal error completion to the results channel writer if an unexpected error occurs
                            resultsChannel.Writer.TryComplete(ex);
                        }
                    },
                    cancellationToken
                )
            );
        }

        // Wait for the producer and all consumers to finish
        // Need a Task to monitor producer and completion of consumers before closing results channel
        _ = Task.Run(
            async () =>
            {
                await producerTask; // Wait for producer first
                await Task.WhenAll(consumerTasks); // Then wait for all consumers
                resultsChannel.Writer.TryComplete(); // Mark results channel complete
            },
            cancellationToken
        );

        // Read results from the results channel and yield them
        await foreach (var paper in resultsChannel.Reader.ReadAllAsync(cancellationToken))
            yield return paper;

        logger.LogInformation("Finished processing sectioned layout using Channels.");
    }

    private async IAsyncEnumerable<Paper> ProcessNonSectionedLayoutAsync(
        HtmlNode tocWrapperNode,
        string pbContext,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        logger.LogInformation("Processing as Non-Sectioned Layout (Paginated See More)");

        // 1. Parse papers initially visible on the page
        logger.LogInformation("Parsing initially visible papers...");
        int initialPaperCount = 0;
        await foreach (
            var paper in acmPaperParser.ParsePapersFromNodeAsync(tocWrapperNode, cancellationToken)
        )
        {
            yield return paper;
            initialPaperCount++;
        }
        logger.LogInformation("Parsed {Count} initial papers.", initialPaperCount);

        // 2. Process 'See More' sequence
        // Initial setup: Find the first 'See More' button and necessary IDs/context
        var firstSeeMoreDiv = tocWrapperNode.SelectSingleNode(
            ".//div[contains(@class, 'see_more')]"
        );
        var firstShowMoreButton = firstSeeMoreDiv?.SelectSingleNode(
            ".//button[contains(@class, 'showMoreProceedings')]"
        );

        string? currentDataUrl = firstShowMoreButton?.Attributes["data-url"]?.Value;
        string? currentDataDoi = firstShowMoreButton?.Attributes["data-doi"]?.Value;
        string? currentDataId = firstShowMoreButton?.Attributes["data-id"]?.Value;
        string? currentWidgetId = tocWrapperNode.Attributes["data-widgetid"]?.Value;

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

        // Loop as long as we have a data-id for the next request
        using var httpClient = httpClientFactory.CreateClient("acm-scraper");
        while (!string.IsNullOrEmpty(currentDataId))
        {
            logger.LogInformation("Fetching 'See More' chunk with ID: {DataId}", currentDataId);
            string? responseHtml = null;
            string? nextDataId = null; // ID for the *next* iteration

            try
            {
                var seeMoreUrl =
                    $"{currentDataUrl}?id={currentDataId}&doi={HttpUtility.UrlEncode(currentDataDoi)}&widgetId={currentWidgetId}&pbContext={HttpUtility.UrlEncode(pbContext)}";
                logger.LogDebug("Requesting 'See More' URL: {SeeMoreUrl}", seeMoreUrl);

                responseHtml = await httpClient.GetStringAsync(seeMoreUrl, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error fetching 'See More' chunk with ID {DataId}",
                    currentDataId
                );
                break; // Stop processing 'See More' on error
            }

            if (string.IsNullOrEmpty(responseHtml))
            {
                logger.LogWarning(
                    "Received empty response for 'See More' chunk ID {DataId}",
                    currentDataId
                );
                // Don't necessarily break here, maybe the next ID is present
            }
            else
            {
                var chunkDoc = new HtmlDocument();
                chunkDoc.LoadHtml(responseHtml);

                // Parse and yield papers from the current chunk
                int chunkPaperCount = 0;
                await foreach (
                    var paper in acmPaperParser.ParsePapersFromNodeAsync(
                        chunkDoc.DocumentNode,
                        cancellationToken
                    )
                )
                {
                    yield return paper;
                    chunkPaperCount++;
                }
                logger.LogDebug(
                    "Parsed {Count} papers from chunk ID {DataId}",
                    chunkPaperCount,
                    currentDataId
                );
            }

            // Find the NEXT 'See More' button within this response's HTML (handle null responseHtml)
            if (!string.IsNullOrEmpty(responseHtml))
            {
                var chunkDocForNextButton = new HtmlDocument(); // Need to parse again or reuse chunkDoc if not null
                chunkDocForNextButton.LoadHtml(responseHtml);
                var nextSeeMoreDiv = chunkDocForNextButton.DocumentNode.SelectSingleNode(
                    "//div[contains(@class, 'see_more')]"
                ); // Search within the response doc
                var nextShowMoreButton = nextSeeMoreDiv?.SelectSingleNode(
                    ".//button[contains(@class, 'showMoreProceedings')]"
                );
                nextDataId = nextShowMoreButton?.Attributes["data-id"]?.Value;
            }
            else
            {
                // If responseHtml was null/empty, we can't find the next button in it.
                nextDataId = null;
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
