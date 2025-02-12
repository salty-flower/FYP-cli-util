using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using DataCollection.Models;
using HtmlAgilityPack;

namespace DataCollection.Services;

class AcmScraper(IHttpClientFactory httpClientFactory)
{
    public async Task<List<Paper>> GetSectionPapers(string proceedingDOI = "10.1145/3597503")
    {
        using var httpClient = httpClientFactory.CreateClient("acm-scraper");
        var proceedingUrl = $"/doi/proceedings/{proceedingDOI}";
        var rootHtml = await httpClient.GetStringAsync(proceedingUrl);
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

        var results = sectionNodes
            .Where(n => n.Attributes.Count == 1)
            .AsParallel()
            .AsUnordered()
            .Select(async sectionNode =>
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
                    proceedingDOI
                );
                return GetPapersFromSection(sectionHtml);
            });

        return [.. (await Task.WhenAll(results)).SelectMany(papers => papers)];
    }

    public async Task DownloadPapersAsync(IEnumerable<Paper> papers, string baseDir)
    {
        using var httpClient = httpClientFactory.CreateClient("acm-scraper");
        // ensure exist
        var dir = new DirectoryInfo(baseDir);
        if (!dir.Exists)
            dir.Create();

        await Parallel.ForEachAsync(
            papers
                .Select(paper => new
                {
                    Link = paper.DownloadLink,
                    Path = Path.Combine(dir.FullName, paper.SanitizedDoi + ".pdf")
                })
                // if non-existent and non-empty, skip
                .Where(p => !File.Exists(p.Path) && new FileInfo(p.Path).Length > 0),
            async (p, ct) =>
            {
                var pdfStream = await httpClient.GetStreamAsync(p.Link, ct);
                using var fileStream = File.Create(p.Path);
                await pdfStream.CopyToAsync(fileStream, ct);
            }
        );
    }

    private async Task<string> GetSectionAsync(
        string sectionTocHeading,
        string sectionDoi,
        string rootDataWidgetId,
        string proceedingDOI
    )
    {
        using var httpClient = httpClientFactory.CreateClient("acm-scraper");
        var sectionUrl =
            $"/pb/widgets/lazyLoadTOC?tocHeading={sectionTocHeading}&widgetId={rootDataWidgetId}&doi={HttpUtility.UrlEncode(sectionDoi)}&pbContext=%3Btaxonomy%3Ataxonomy%3Aconference-collections%3Bissue%3Aissue%3Adoi%5C%3A{HttpUtility.UrlEncode(proceedingDOI)}%3Bwgroup%3Astring%3AACM%20Publication%20Websites%3BgroupTopic%3Atopic%3Aacm-pubtype%3Eproceeding%3Bcsubtype%3Astring%3AConference%20Proceedings%3Bpage%3Astring%3ABook%20Page%3Bwebsite%3Awebsite%3Adl-site%3Bctype%3Astring%3ABook%20Content%3Btopic%3Atopic%3Aconference-collections%3Eicse%3Barticle%3Aarticle%3Adoi%5C%3A{HttpUtility.UrlEncode(proceedingDOI)}%3Bjournal%3Ajournal%3Aacmconferences%3BpageGroup%3Astring%3APublication%20Pages";
        var sectionHtml = await httpClient.GetStringAsync(sectionUrl);
        return sectionHtml;
    }

    private static List<Paper> GetPapersFromSection(string sectionHtml)
    {
        var sectionDoc = new HtmlDocument();
        sectionDoc.LoadHtml(sectionHtml);
        var paperNodes = sectionDoc.DocumentNode.SelectNodes(
            "//div[contains(@class, 'issue-item-container')]"
        );
        var papers = new List<Paper>();
        foreach (var paperNode in paperNodes)
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
            var @abstract = paperNode.SelectSingleNode(".//p").InnerText;
            var url = paperNode.SelectSingleNode(".//a").Attributes["href"].Value;
            papers.Add(
                new Paper
                {
                    Title = title,
                    Authors = authors,
                    Abstract = @abstract,
                    Url = url,
                    Doi = doi
                }
            );
        }
        return papers;
    }
}
