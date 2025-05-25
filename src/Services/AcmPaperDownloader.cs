using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DataCollection.Models;
using Microsoft.Extensions.Logging;

namespace DataCollection.Services;

public class AcmPaperDownloader(
    IHttpClientFactory httpClientFactory,
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
            // if non-existent or empty, download
            .Where(p => !File.Exists(p.Path) || new FileInfo(p.Path).Length == 0)
            .ToList();

        logger.LogInformation(
            "Downloading {Count} papers to {Directory}",
            filteredPapers.Count,
            baseDir
        );

        await Parallel.ForEachAsync(
            filteredPapers,
            cancellationToken,
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
                    using var fileStream = File.Create(p.Path);
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
                    {
                        try
                        {
                            File.Delete(p.Path);
                            logger.LogDebug("Successfully deleted partially downloaded file");
                        }
                        catch (Exception deleteEx)
                        {
                            logger.LogWarning(deleteEx, "Error deleting partially downloaded file");
                        }
                    }
                }
            }
        );
    }
}
