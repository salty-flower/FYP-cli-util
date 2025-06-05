using System.Runtime.CompilerServices;
using System.Web;
using DataCollection.Core.Models;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace DataCollection.Application.Features.PaperAnalysis;

public class AcmPaperParser(IHttpClientFactory httpClientFactory, ILogger<AcmPaperParser> logger)
{
    public async IAsyncEnumerable<Paper> ParsePapersFromNodeAsync(
        HtmlNode parentNode,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var paperNodes = parentNode.SelectNodes(".//div[contains(@class, 'issue-item-container')]");

        if (paperNodes == null)
            yield break;

        foreach (var paperNode in paperNodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Paper? paper = null; // Declare paper variable outside try
            try
            {
                // Check for H3 first (common in non-sectioned), then fallback to H5
                var titleNode =
                    paperNode.SelectSingleNode(".//h3[contains(@class, 'issue-item__title')]/.//a")
                    ?? paperNode.SelectSingleNode(
                        ".//h5[contains(@class, 'issue-item__title')]/.//a"
                    );

                if (titleNode == null)
                {
                    // Reduce log level as this happens for non-paper items
                    logger.LogTrace("Skipping node, title link not found within h3 or h5.");
                    continue;
                }

                var title = HttpUtility.HtmlDecode(titleNode.InnerText.Trim()); // Decode HTML entities and trim
                var doiLink = titleNode.Attributes["href"]?.Value;
                if (string.IsNullOrEmpty(doiLink) || !doiLink.Contains("/doi/"))
                {
                    logger.LogWarning(
                        "Skipping paper node, valid DOI link not found in title href: {Title}",
                        title
                    );
                    continue;
                }
                var doi = doiLink.Replace("/doi/", "");

                // Handle cases where author list might be missing or empty
                var authorNodes = paperNode
                    .SelectSingleNode(".//ul[contains(@class, 'loa')]")
                    ?.SelectNodes(".//li/a"); // More specific selector for author links
                var authors =
                    authorNodes
                        ?.Select(aNode =>
                            HttpUtility.HtmlDecode(aNode.InnerText.Trim()).TrimEnd(',')
                        )
                        .ToArray() ?? []; // Decode and trim comma

                var abstractText = await GetPaperAbstractAsync(doi, cancellationToken);
                var url = $"https://dl.acm.org{doiLink}"; // Construct full URL

                // Assign to paper variable instead of yielding directly
                paper = new Paper
                {
                    Title = title,
                    Authors = authors,
                    Abstract = abstractText,
                    Url = url,
                    Doi = doi,
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error extracting paper data from node.");
            }

            if (paper != null)
                yield return paper;
        }
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
        // Two XPaths for abstract to be more robust
        var abstractNode =
            paperDoc.DocumentNode.SelectSingleNode("//div[contains(@class, 'abstractSection')]//p")
            ?? paperDoc.DocumentNode.SelectSingleNode(
                "//div[@id='abstracts']//div[contains(@class, 'abstract')]/p"
            );
        return HttpUtility.HtmlDecode(abstractNode?.InnerText?.Trim()) ?? "Abstract not available";
    }
}
