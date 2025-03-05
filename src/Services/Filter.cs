using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using DataCollection.Models;
using PdfPlumber;
using Python.Runtime;

namespace DataCollection.Services;

class Filter(DirectoryInfo paperDir)
{
    public PdfData[] FilterPapers()
    {
        var pdfDataList = paperDir
            .GetFiles("*.pdf")
            .AsParallel()
            .AsUnordered()
            .Select(p =>
            {
                try
                {
                    var po = PDF.open(p.FullName);
                    var tables = po.Pages.Select(p => p.extractTables());
                    var texts = po.Pages.Select(p => p.extractText());

                    var d = new PdfData
                    {
                        FileName = p.Name,
                        Texts = texts.ToArray(),
                        Tables = tables
                            .Select(t =>
                                t.Select(tt =>
                                        tt.Cells.Select(ccc => ccc.Cast<PlainCell>().ToArray())
                                            .ToArray()
                                    )
                                    .ToArray()
                            )
                            .ToArray(),
                    };
                    po.close();

                    Console.WriteLine($"Dumped {p.Name}");
                    return d;
                }
                catch (PythonException e)
                {
                    Console.WriteLine($"Error dumping {p.Name}: {e.Message}");
                    return null;
                }
            })
            .Where(d => d != null)
            .Cast<PdfData>()
            .ToArray();

        return pdfDataList;
    }

    public async IAsyncEnumerable<PdfData> FilterPapersAsync(
        ReadOnlyDictionary<string, bool>? skipMap
    )
    {
        var pdfFiles = paperDir
            .GetFiles("*.pdf")
            .Where(f => skipMap == null || !skipMap.ContainsKey(f.Name));

        foreach (var pdfFile in pdfFiles)
        {
            PdfData result = null;
            PDF po = null;
            try
            {
                po = PDF.open(pdfFile.FullName);

                // Process pages in parallel but within each file
                var pages = po.Pages.ToList(); // Materialize pages once

                //var tables = pages.Select(p => p.extractTables()).ToList();
                var texts = pages.Select(p => p.extractText()).ToArray();

                result = new PdfData
                {
                    FileName = pdfFile.Name,
                    Texts = texts,
                    Tables = [],
                    //Tables = tables.Select(t =>
                    //    t.Select(tt =>
                    //        tt.Cells.Select(ccc =>
                    //            ccc.Cast<PlainCell>().ToArray()
                    //        ).ToArray()
                    //    ).ToArray()
                    //).ToArray()
                };

                Console.WriteLine($"Dumped {pdfFile.Name}");
            }
            catch (PythonException e)
            {
                Console.WriteLine($"Error dumping {pdfFile.Name}: {e.Message}");
                continue;
            }
            finally
            {
                if (po != null)
                {
                    po.close();
                }
            }

            if (result != null)
            {
                yield return result;
            }
        }
    }
}
