using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Web;
using DataCollection.Core.Models;
using DataCollection.Infrastructure.Options;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Infrastructure.Clients.ACM;

public class PaperEnricher(
    IHttpClientFactory httpClientFactory,
    IOptions<ParallelismOptions> parallelismOptions,
    ILogger<PaperEnricher> logger
)
{
    private readonly ParallelismOptions _parallelismOptions = parallelismOptions.Value;

    public async IAsyncEnumerable<Paper> EnrichWithAbstractsAsync(
        IAsyncEnumerable<Paper> papers,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var channel = Channel.CreateUnbounded<Paper>();

        var consumerTask = Task.Run(
            async () =>
            {
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = _parallelismOptions.PaperEnrichment,
                    CancellationToken = cancellationToken,
                };
                await Parallel.ForEachAsync(
                    papers,
                    parallelOptions,
                    async (paper, ct) =>
                    {
                        try
                        {
                            var abstractText = await GetPaperAbstractAsync(paper.Doi, ct);
                            await channel.Writer.WriteAsync(
                                paper with
                                {
                                    Abstract = abstractText,
                                },
                                ct
                            );
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(
                                ex,
                                "Failed to enrich paper {Doi} with abstract.",
                                paper.Doi
                            );
                            await channel.Writer.WriteAsync(paper, ct);
                        }
                    }
                );
                channel.Writer.Complete();
            },
            cancellationToken
        );

        await foreach (var paper in channel.Reader.ReadAllAsync(cancellationToken))
            yield return paper;

        await consumerTask;
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
        return HttpUtility.HtmlDecode(abstractNode?.InnerText.Trim()) ?? "Abstract not available";
    }
}
