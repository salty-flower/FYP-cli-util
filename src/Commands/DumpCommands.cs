using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConsoleAppFramework;
using DataCollection.Models;
using DataCollection.Options;
using DataCollection.PdfPlumber;
using DataCollection.Services;
using DataCollection.Utils;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Python.Runtime;
using Serilog.Data;

namespace DataCollection.Commands;

/// <summary>
/// Commands for dumping papers
/// </summary>
[RegisterCommands("dump")]
public class DumpCommands(ILogger<ScrapeCommands> logger, IOptions<PathsOptions> pathsOptions)
{
    private readonly PathsOptions _pathsOptions = pathsOptions.Value;

    /// <summary>
    /// Process and analyze PDF files
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task PDF(CancellationToken cancellationToken = default)
    {
        var pdfDataDir = new DirectoryInfo(_pathsOptions.PdfDataDir);
        var paperBinDir = new DirectoryInfo(_pathsOptions.PaperBinDir);

        if (!pdfDataDir.Exists)
            pdfDataDir.Create();

        // Configure Python environment
        InitializePythonEngine();

        // Get already processed files
        var alreadyDumped = pdfDataDir.GetFiles("*.bin").Select(f => f.Name.Replace(".bin", ""));
        var alreadyDumpedDict = alreadyDumped.ToDictionary(p => p, _ => true).AsReadOnly();

        await foreach (
            var pdfData in FilterPapersAsync(paperBinDir, alreadyDumpedDict)
                .WithCancellation(cancellationToken)
        )
        {
            // Serialize and save the PDF data
            var bin = MemoryPackSerializer.Serialize(pdfData);
            var binPath = Path.Combine(pdfDataDir.FullName, $"{pdfData.FileName}.bin");
            await File.WriteAllBytesAsync(binPath, bin, cancellationToken);
        }

        logger.LogInformation("PDFPlumber dump completed");
    }

    private void InitializePythonEngine()
    {
        Runtime.PythonDLL = _pathsOptions.PythonDLL;
        PythonEngine.Initialize();
        PythonEngine.BeginAllowThreads();
    }

    private async IAsyncEnumerable<PdfData> FilterPapersAsync(
        DirectoryInfo paperDir,
        ReadOnlyDictionary<string, bool>? skipMap
    )
    {
        var pdfFiles = paperDir
            .GetFiles("*.pdf")
            .Where(f => skipMap == null || !skipMap.ContainsKey(f.Name));

        logger.LogInformation(
            "Extracting  PDF files: {total} (total) = {skipped} (skipped) + {actual} (actual)",
            pdfFiles.Count() + skipMap?.Count ?? 0,
            skipMap?.Count ?? 0,
            pdfFiles.Count()
        );
        foreach (var pdfFile in pdfFiles)
        {
            PDF? po = null;
            try
            {
                po = PdfPlumber.PDF.open(pdfFile.FullName);
            }
            catch (PythonException e)
            {
                logger.LogWarning("Can't extract {f}: {m}", pdfFile.Name, e.Message);
                continue;
            }
            var pages = po.Pages.ToList(); // Materialize pages once

            var tables = pages.Select(p => p.extractTables()).ToList();
            var texts = pages.Select(p => p.extractText()).ToArray();
            var textLines = pages.Select(p => p.extractTextLines()).ToArray();

            var result = new PdfData
            {
                FileName = pdfFile.Name,
                Texts = texts,
                Tables = tables
                    .Select(t =>
                        t.Select(tt =>
                                tt.Cells.Select(ccc => ccc.Select(c => (PlainCell)c).ToArray())
                                    .ToArray()
                            )
                            .ToArray()
                    )
                    .ToArray(),
                TextLines = textLines,
            };

            logger.LogInformation("Extracted {FileName}", pdfFile.Name);

            yield return result;

            po?.close();
            po?.Dispose();
        }
    }
}
