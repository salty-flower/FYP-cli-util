using System.Web;
using DataCollection.Core.Models;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace DataCollection.Infrastructure.Clients.ACM;

public class AcmPaperParser(ILogger<AcmPaperParser> logger)
{
    public IEnumerable<Paper> ParsePapersFromNode(HtmlNode parentNode)
    {
        var paperNodes = parentNode.SelectNodes(".//div[contains(@class, 'issue-item-container')]");

        if (paperNodes == null)
            yield break;

        foreach (var paperNode in paperNodes)
        {
            // Check for H3 first (common in non-sectioned), then fallback to H5
            var titleNode =
                paperNode.SelectSingleNode(".//h3[contains(@class, 'issue-item__title')]/.//a")
                ?? paperNode.SelectSingleNode(".//h5[contains(@class, 'issue-item__title')]/.//a");

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
                    ?.Select(aNode => HttpUtility.HtmlDecode(aNode.InnerText.Trim()).TrimEnd(','))
                    .ToArray() ?? []; // Decode and trim comma

            var url = $"https://dl.acm.org{doiLink}"; // Construct full URL

            // Assign to paper variable instead of yielding directly
            var paper = new Paper // Declare paper variable outside try
            {
                Title = title,
                Authors = authors,
                Url = url,
                Doi = doi,
            };

            yield return paper;
        }
    }
}
