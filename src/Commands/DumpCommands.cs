using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
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
using Spectre.Console;

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

        dynamic fitz;
        using (Py.GIL())
            fitz = Py.Import("fitz");

        foreach (var pdfFile in pdfFiles)
            yield return ProcessSinglePdf(pdfFile, fitz);
    }

    /// <summary>
    /// Process a single PDF file and extract its content
    /// </summary>
    /// <param name="pdfFile">The PDF file to process</param>
    /// <returns>A PdfData object containing the extracted data</returns>
    private PdfData ProcessSinglePdf(FileInfo pdfFile, dynamic fitz)
    {
        using (Py.GIL())
        {
            dynamic document = fitz.open(pdfFile.FullName);

            var proc = Enumerable
                .Range(0, (int)document.page_count.As<int>())
                .Select(i => document[i])
                .Select(page =>
                {
                    // Extract text
                    string text = page.get_text().As<string>();
                    var pyTextBlocks = page.get_text("dict")["blocks"];
                    var textLines = Enumerable
                        .Range(0, (int)pyTextBlocks.__len__().As<int>())
                        .Select(b => pyTextBlocks[b])
                        .Where(block => block["type"].As<int>() == 0) // Only text blocks
                        .Select(block => block["lines"])
                        .SelectMany(pyLines =>
                            Enumerable
                                .Range(0, (int)pyLines.__len__().As<int>())
                                .Select(l => pyLines[l])
                                .Select(line =>
                                {
                                    var spans = line["spans"];

                                    // Concatenate all span texts
                                    var sb = new StringBuilder();
                                    for (int s = 0; s < (int)spans.__len__().As<int>(); s++)
                                        sb.Append(spans[s]["text"].As<string>());

                                    return new
                                    {
                                        LineText = sb.ToString(),
                                        BBox = line["bbox"].As<dynamic>(),
                                    };
                                })
                                .Where(item => !string.IsNullOrWhiteSpace(item.LineText))
                                .Select(item => new MatchObject(
                                    item.LineText,
                                    item.BBox[0].As<double>(), // x0
                                    item.BBox[1].As<double>(), // top
                                    item.BBox[2].As<double>(), // x1
                                    item.BBox[3].As<double>() // bottom
                                ))
                        )
                        .ToArray();

                    return new { Text = text, TextLines = textLines };
                })
                .ToList();

            document.close();

            var result = new PdfData
            {
                FileName = pdfFile.Name,
                Texts = proc.Select(p => p.Text).ToArray(),
                TextLines = proc.Select(p => p.TextLines).ToArray(),
            };

            logger.LogInformation("Extracted {FileName}", pdfFile.Name);

            return result;
        }
    }
}
