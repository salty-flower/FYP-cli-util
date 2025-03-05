﻿using System;
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
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;

namespace DataCollection.Services;

class AcmScraper(IHttpClientFactory httpClientFactory, IConfiguration configuration)
{
    private readonly int _sectionParallelism = configuration.GetValue<int>(
        "Scraper:Parallelism:SectionProcessing",
        3
    );
    private readonly int _downloadParallelism = configuration.GetValue<int>(
        "Scraper:Parallelism:Downloads",
        5
    );

    // Keep the original method for backward compatibility
    public async Task<List<Paper>> GetSectionPapers(string proceedingDOI = "10.1145/3597503")
    {
        var papers = new List<Paper>();
        await foreach (var paper in GetSectionPapersAsync(proceedingDOI))
        {
            papers.Add(paper);
        }
        return papers;
    }

    // New streaming method that yields papers as they are processed
    public async IAsyncEnumerable<Paper> GetSectionPapersAsync(
        string proceedingDOI = "10.1145/3597503",
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        using var httpClient = httpClientFactory.CreateClient("acm-scraper");
        var proceedingUrl = $"/doi/proceedings/{proceedingDOI}";
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
                    var sectionHtml = await GetSectionAsync(
                        sectionHeadingId,
                        sectionDoi,
                        rootDataWidgetId,
                        proceedingDOI,
                        cancellationToken
                    );
                    var papers = await GetPapersFromSection(sectionHtml, cancellationToken);
                    return (sectionNode, papers);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing section: {ex.Message}");
                    return (sectionNode, new List<Paper>());
                }
            },
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = _sectionParallelism,
                CancellationToken = cancellationToken,
            }
        );

        // Link blocks
        sectionBuffer.LinkTo(
            sectionProcessor,
            new DataflowLinkOptions { PropagateCompletion = true }
        );

        // Post all section nodes to be processed
        foreach (var node in sectionNodes.Where(n => n.Attributes.Count == 1))
        {
            await sectionBuffer.SendAsync(node, cancellationToken);
        }
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

        // Use SemaphoreSlim to limit concurrent downloads
        using var semaphore = new SemaphoreSlim(_downloadParallelism);

        await Parallel.ForEachAsync(
            filteredPapers,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = _downloadParallelism,
            },
            async (p, ct) =>
            {
                try
                {
                    await semaphore.WaitAsync(ct);
                    Console.WriteLine($"Downloading: {p.Paper.Title} ({Path.GetFileName(p.Path)})");
                    var pdfStream = await httpClient.GetStreamAsync(p.Link, ct);
                    using var fileStream = File.Create(p.Path);
                    await pdfStream.CopyToAsync(fileStream, ct);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"Error downloading {Path.GetFileName(p.Path)}: {ex.Message}"
                    );
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
                Console.WriteLine($"Error extracting paper data: {ex.Message}");
            }
        }
        return papers;
    }
}
