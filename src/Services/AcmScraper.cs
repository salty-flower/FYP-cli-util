using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Web;
using DataCollection.Models;
using DataCollection.Options;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Services;

public class AcmScraper(
    IHttpClientFactory httpClientFactory,
    IOptions<ScraperOptions> options,
    IOptionsSnapshot<ParallelismOptions> parallelOpt,
    ILogger<AcmScraper> logger
)
{
    private readonly ScraperOptions _options = options.Value;

    // New streaming method that yields papers as they are processed
    public async IAsyncEnumerable<Paper> GetSectionPapersAsync(
        string proceedingDOI = "10.1145/3597503",
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        using var httpClient = httpClientFactory.CreateClient("acm-scraper");
        var proceedingUrl = $"/doi/proceedings/{proceedingDOI}";

        logger.LogInformation("Fetching proceedings from {Url}", proceedingUrl);
        var rootHtml = await httpClient.GetStringAsync(proceedingUrl, cancellationToken);
        var rootDoc = new HtmlDocument();
        rootDoc.LoadHtml(rootHtml);

        var tocWrapperNode = rootDoc.DocumentNode.SelectSingleNode(
            ".//div[contains(@class, 'table-of-content-wrapper')]"
        );
        var rootDataWidgetId = tocWrapperNode.Attributes["data-widgetid"].Value;

        // get sections
        var sectionNodes = tocWrapperNode.SelectNodes(
            "//div[@class = 'toc__section accordion-tabbed__tab']"
        );

        logger.LogInformation("Found {Count} sections to process", sectionNodes?.Count ?? 0);

        // Create a DataFlow pipeline for processing sections in parallel with controlled throughput
        var sectionBuffer = new BufferBlock<HtmlNode>(
            new DataflowBlockOptions { CancellationToken = cancellationToken }
        );

        var sectionProcessor = new TransformBlock<HtmlNode, (HtmlNode Node, List<Paper> Papers)>(
            async sectionNode =>
            {
                try
                {
                    var sectionDoi = sectionNode
                        .SelectSingleNode(".//div[contains(@class, 'accordion-lazy')]")
                        .Attributes["data-doi"]
                        .Value;
                    var sectionHeadingId = sectionNode
                        .SelectSingleNode(
                            ".//a[contains(@class, 'section__title accordion-tabbed__control left-bordered-title')]"
                        )
                        .Attributes["id"]
                        .Value;

                    logger.LogDebug(
                        "Processing section {SectionId} with DOI {SectionDoi}",
                        sectionHeadingId,
                        sectionDoi
                    );

                    var sectionHtml = await GetSectionAsync(
                        sectionHeadingId,
                        sectionDoi,
                        rootDataWidgetId,
                        proceedingDOI,
                        cancellationToken
                    );
                    var papers = await GetPapersFromSection(sectionHtml, cancellationToken);

                    logger.LogInformation(
                        "Found {Count} papers in section {SectionId}",
                        papers.Count,
                        sectionHeadingId
                    );

                    return (sectionNode, papers);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing section");
                    return (sectionNode, new List<Paper>());
                }
            },
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = parallelOpt.Value.SectionProcessing,
                CancellationToken = cancellationToken,
            }
        );

        // Link blocks
        sectionBuffer.LinkTo(
            sectionProcessor,
            new DataflowLinkOptions { PropagateCompletion = true }
        );

        // Post all section nodes to be processed
        if (sectionNodes != null)
            foreach (var node in sectionNodes.Where(n => n.Attributes.Count == 1))
                await sectionBuffer.SendAsync(node, cancellationToken);

        sectionBuffer.Complete();

        // Process results as they complete
        while (await sectionProcessor.OutputAvailableAsync(cancellationToken))
        {
            var (_, papers) = await sectionProcessor.ReceiveAsync(cancellationToken);
            foreach (var paper in papers)
            {
                yield return paper;
            }
        }
    }

    public async Task DownloadPapersAsync(
        IEnumerable<Paper> papers,
        string baseDir,
        CancellationToken cancellationToken = default
    )
    {
        using var httpClient = httpClientFactory.CreateClient("acm-scraper");
        // ensure directory exists
        var dir = new DirectoryInfo(baseDir);
        if (!dir.Exists)
            dir.Create();

        var filteredPapers = papers
            .Select(paper => new
            {
                Link = paper.DownloadLink,
                Path = Path.Combine(dir.FullName, paper.SanitizedDoi + ".pdf"),
                Paper = paper,
            })
            // if non-existent or empty, download
            .Where(p => !File.Exists(p.Path) || new FileInfo(p.Path).Length == 0)
            .ToList();

        logger.LogInformation(
            "Downloading {Count} papers to {Directory}",
            filteredPapers.Count,
            baseDir
        );

        // Use SemaphoreSlim to limit concurrent downloads
        using var semaphore = new SemaphoreSlim(parallelOpt.Value.Downloads);

        // Track last download time for rate limiting
        var lastDownloadTimes = new ConcurrentDictionary<int, DateTime>();
        var downloadDelayMs = parallelOpt.Value.DownloadDelayMs;
        var randomGen = new Random();

        logger.LogInformation(
            "Rate limiting configured with delay of {DelayMs}ms between downloads",
            downloadDelayMs
        );

        await Parallel.ForEachAsync(
            filteredPapers,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = parallelOpt.Value.Downloads,
            },
            async (p, ct) =>
            {
                try
                {
                    await semaphore.WaitAsync(ct);

                    // Apply rate limiting if configured
                    if (downloadDelayMs > 0)
                    {
                        var threadId = Environment.CurrentManagedThreadId;
                        int remainingDelay = 0;

                        if (lastDownloadTimes.TryGetValue(threadId, out var lastDownload))
                        {
                            var elapsed = (DateTime.UtcNow - lastDownload).TotalMilliseconds;
                            remainingDelay =
                                downloadDelayMs - (int)elapsed + randomGen.Next(downloadDelayMs);
                        }
                        if (remainingDelay > 0)
                            await Task.Delay(remainingDelay, ct);

                        lastDownloadTimes[threadId] = DateTime.UtcNow;
                    }

                    logger.LogInformation(
                        "Downloading: {Title} ({FileName})",
                        p.Paper.Title,
                        Path.GetFileName(p.Path)
                    );

                    var pdfStream = await httpClient.GetStreamAsync(p.Link, ct);
                    using var fileStream = File.Create(p.Path);
                    await pdfStream.CopyToAsync(fileStream, ct);

                    logger.LogDebug("Successfully downloaded {FileName}", Path.GetFileName(p.Path));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error downloading {FileName}", Path.GetFileName(p.Path));
                }
                finally
                {
                    semaphore.Release();
                }
            }
        );
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

    private async Task<string> GetPaperAbstractAsync(
        string paperDoi,
        CancellationToken cancellationToken = default
    )
    {
        using var httpClient = httpClientFactory.CreateClient("acm-scraper");
        var paperUrl = $"/doi/{paperDoi}";
        var paperHtml = await httpClient.GetStringAsync(paperUrl, cancellationToken);
        var paperDoc = new HtmlDocument();
        paperDoc.LoadHtml(paperHtml);
        var abstractNode = paperDoc.DocumentNode.SelectSingleNode(
            "//div[contains(@id, 'abstracts')]//section//div"
        );
        return abstractNode?.InnerText ?? "Abstract not available";
    }

    private async Task<List<Paper>> GetPapersFromSection(
        string sectionHtml,
        CancellationToken cancellationToken = default
    )
    {
        var sectionDoc = new HtmlDocument();
        sectionDoc.LoadHtml(sectionHtml);
        var paperNodes = sectionDoc.DocumentNode.SelectNodes(
            "//div[contains(@class, 'issue-item-container')]"
        );

        if (paperNodes == null)
            return new List<Paper>();

        var papers = new List<Paper>();
        foreach (var paperNode in paperNodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var titleNode = paperNode
                    .SelectSingleNode(".//h5[contains(@class, 'issue-item__title')]")
                    .SelectSingleNode(".//a");
                var title = titleNode.InnerText;
                var doi = titleNode.Attributes["href"].Value.Replace("/doi/", "");
                var authors = paperNode
                    .SelectSingleNode(".//ul")
                    .SelectNodes(".//li")
                    .Select(li => li.InnerText.TrimEnd(','))
                    .ToArray();
                var @abstract = await GetPaperAbstractAsync(doi, cancellationToken);
                var url = paperNode.SelectSingleNode(".//a").Attributes["href"].Value;
                papers.Add(
                    new Paper
                    {
                        Title = title,
                        Authors = authors,
                        Abstract = @abstract,
                        Url = url,
                        Doi = doi,
                    }
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error extracting paper data");
            }
        }
        return papers;
    }
}
