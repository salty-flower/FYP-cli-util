using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConsoleAppFramework;
using DataCollection.Filters;
using DataCollection.Models;
using DataCollection.Options;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Python.Runtime;

namespace DataCollection.Commands;

/// <summary>
/// Commands for dumping papers
/// </summary>
[RegisterCommands("dump")]
[ConsoleAppFilter<PathsOptions.Filter>]
public class DumpCommands(ILogger<ScrapeCommands> logger, IOptions<PathsOptions> pathsOptions)
{
    private readonly PathsOptions _pathsOptions = pathsOptions.Value;

    /// <summary>
    /// Process and analyze PDF files
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    [ConsoleAppFilter<PythonEngineInitFilter>]
    public async Task PDF(CancellationToken cancellationToken = default)
    {
        var pdfDataDir = new DirectoryInfo(_pathsOptions.PdfDataDir);
        var paperBinDir = new DirectoryInfo(_pathsOptions.PaperBinDir);

        // Get already processed files
        var alreadyDumped = pdfDataDir.GetFiles("*.bin").Select(f => f.Name.Replace(".bin", ""));
        var alreadyDumpedDict = alreadyDumped.ToDictionary(p => p, _ => true).AsReadOnly();

        foreach (var pdfData in ExtractPdfData(paperBinDir, alreadyDumpedDict))
        {
            // Serialize and save the PDF data
            var bin = MemoryPackSerializer.Serialize(pdfData);
            var binPath = Path.Combine(pdfDataDir.FullName, $"{pdfData.FileName}.bin");
            await File.WriteAllBytesAsync(binPath, bin, cancellationToken);

            GC.Collect();
        }

        logger.LogInformation("PyMuPDF dump completed");
    }

    private IEnumerable<PdfData> ExtractPdfData(
        DirectoryInfo paperDir,
        ReadOnlyDictionary<string, bool>? skipMap
    )
    {
        var pdfFiles = paperDir
            .GetFiles("*.pdf")
            .Where(f => skipMap == null || !skipMap.ContainsKey(f.Name));

        logger.LogInformation(
            "Extracting PDF files: {total} (total) = {skipped} (skipped) + {actual} (actual)",
            pdfFiles.Count() + skipMap?.Count ?? 0,
            skipMap?.Count ?? 0,
            pdfFiles.Count()
        );

        foreach (var pdfFile in pdfFiles)
            yield return ProcessSinglePdf(pdfFile);
    }

    /// <summary>
    /// Process a single PDF file and extract its content
    /// </summary>
    /// <param name="pdfFile">The PDF file to process</param>
    /// <returns>A PdfData object containing the extracted data</returns>
    private PdfData ProcessSinglePdf(FileInfo pdfFile)
    {
        using (Py.GIL())
        {
            // Import fitz (PyMuPDF)
            dynamic fitz = Py.Import("fitz");
            dynamic document = fitz.open(pdfFile.FullName);

            int pageCount = document.page_count.As<int>();
            string[] texts = new string[pageCount];
            MatchObject[][] textLines = new MatchObject[pageCount][];

            // Process each page
            for (int i = 0; i < pageCount; i++)
            {
                dynamic page = document[i];

                // Extract text
                texts[i] = page.get_text().As<string>();

                // Extract text lines
                var pyTextBlocks = page.get_text("dict")["blocks"];
                List<MatchObject> pageTextLines = new List<MatchObject>();

                int blockCount = pyTextBlocks.__len__().As<int>();
                for (int b = 0; b < blockCount; b++)
                {
                    var block = pyTextBlocks[b];
                    if (block["type"].As<int>() == 0) // Text block
                    {
                        var pyLines = block["lines"];
                        int lineCount = pyLines.__len__().As<int>();
                        for (int l = 0; l < lineCount; l++)
                        {
                            var line = pyLines[l];
                            var lineText = "";
                            var spans = line["spans"];
                            int spanCount = spans.__len__().As<int>();
                            for (int s = 0; s < spanCount; s++)
                            {
                                lineText += spans[s]["text"].As<string>();
                            }

                            var bbox = line["bbox"].As<dynamic>();
                            if (!string.IsNullOrWhiteSpace(lineText))
                            {
                                pageTextLines.Add(
                                    new MatchObject(
                                        lineText,
                                        bbox[0].As<double>(), // x0
                                        bbox[1].As<double>(), // top
                                        bbox[2].As<double>(), // x1
                                        bbox[3].As<double>() // bottom
                                    )
                                );
                            }
                        }
                    }
                }

                textLines[i] = pageTextLines.ToArray();
            }

            document.close();

            var result = new PdfData
            {
                FileName = pdfFile.Name,
                Texts = texts,
                TextLines = textLines,
            };

            logger.LogInformation("Extracted {FileName}", pdfFile.Name);

            return result;
        }
    }
}
