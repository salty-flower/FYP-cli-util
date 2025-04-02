using System;
using System.Collections.Generic;
using System.IO;
using DataCollection.Models;
using MemoryPack;
using Microsoft.Extensions.Logging;

namespace DataCollection.Services;

/// <summary>
/// Service for loading data from files
/// </summary>
public class DataLoadingService(
    ILogger<DataLoadingService> logger,
    PdfDescriptionService pdfDescriptionService
)
{
    /// <summary>
    /// Load PDF data from a directory
    /// </summary>
    public List<PdfData> LoadPdfDataFromDirectory(
        string directoryPath,
        string? paperMetadataDir = null
    )
    {
        var pdfDataDir = new DirectoryInfo(directoryPath);
        var pdfDataList = new List<PdfData>();

        if (!pdfDataDir.Exists || pdfDataDir.GetFiles("*.bin").Length == 0)
        {
            return pdfDataList;
        }

        logger.LogInformation("Loading PDF data...");

        foreach (var file in pdfDataDir.GetFiles("*.bin"))
        {
            try
            {
                var bin = File.ReadAllBytes(file.FullName);
                var pdfData = MemoryPackSerializer.Deserialize<PdfData>(bin);
                if (pdfData != null)
                {
                    pdfDataList.Add(pdfData);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    "Error loading PDF data {FileName}: {Error}",
                    file.Name,
                    ex.Message
                );
            }
        }

        // Try to load paper metadata if a path is provided
        if (!string.IsNullOrEmpty(paperMetadataDir))
        {
            var papers = LoadPapersFromMetadata(paperMetadataDir);
            if (papers.Count > 0)
            {
                pdfDescriptionService.UpdatePaperCache(papers);
            }
        }

        logger.LogInformation("Loaded {Count} PDF documents", pdfDataList.Count);
        return pdfDataList;
    }

    /// <summary>
    /// Load papers from metadata directory
    /// </summary>
    public List<Paper> LoadPapersFromMetadata(string metadataPath)
    {
        var metadataDir = new DirectoryInfo(metadataPath);
        var papers = new List<Paper>();

        if (!metadataDir.Exists)
        {
            return papers;
        }

        logger.LogInformation("Loading papers from metadata...");

        foreach (var file in metadataDir.GetFiles("*.bin"))
        {
            try
            {
                var bin = File.ReadAllBytes(file.FullName);
                var paper = MemoryPackSerializer.Deserialize<Paper>(bin);
                if (paper != null)
                {
                    papers.Add(paper);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Error loading paper {FileName}: {Error}", file.Name, ex.Message);
            }
        }

        logger.LogInformation("Loaded {Count} papers", papers.Count);
        return papers;
    }
}
