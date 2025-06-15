using DataCollection.Core.Models;
using DataCollection.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Infrastructure.Clients.ACM;

public class AcmPaperDownloader(
    IHttpClientFactory httpClientFactory,
    IOptionsSnapshot<ParallelismOptions> parallelismOptions,
    ILogger<AcmPaperDownloader> logger
)
{
    public async Task DownloadPapersAsync(
        IEnumerable<Paper> papers,
        string baseDir,
        CancellationToken cancellationToken = default
    )
    {
        using var httpClient = httpClientFactory.CreateClient("acm-scraper");

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
            .Where(p => ShouldDownload(p.Path))
            .ToList();

        logger.LogInformation(
            "Downloading {Count} papers to {Directory}",
            filteredPapers.Count,
            baseDir
        );

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = parallelismOptions.Value.PaperDownloading,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(
            filteredPapers,
            parallelOptions,
            async (p, ct) =>
            {
                try
                {
                    logger.LogInformation(
                        "Downloading: {Title} ({FileName})",
                        p.Paper.Title,
                        Path.GetFileName(p.Path)
                    );

                    var pdfStream = await httpClient.GetStreamAsync(p.Link, ct);
                    await using var fileStream = File.Create(p.Path);
                    await pdfStream.CopyToAsync(fileStream, ct);

                    logger.LogDebug("Successfully downloaded {FileName}", Path.GetFileName(p.Path));
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Error downloading {FileName}, removing partially downloaded file...",
                        Path.GetFileName(p.Path)
                    );

                    if (File.Exists(p.Path))
                        File.Delete(p.Path);
                }
            }
        );
    }

    private static bool ShouldDownload(string path) =>
        !File.Exists(path) || new FileInfo(path).Length == 0;
}
