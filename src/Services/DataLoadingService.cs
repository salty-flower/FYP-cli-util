using System;
using System.Collections.Generic;
using System.IO;
using DataCollection.Models;
using MemoryPack;
using Microsoft.Extensions.Logging;

namespace DataCollection.Services;

/// <summary>
/// Service for loading data from files
/// DEPRECATED: Use DatabaseDataLoadingService instead
/// </summary>
[Obsolete("Use DatabaseDataLoadingService instead for database-based data access")]
public class DataLoadingService(
    ILogger<DataLoadingService> logger,
    PdfDescriptionService pdfDescriptionService
)
{
    private const string BinaryFileExtension = "*.bin";
    private const string PdfBinaryFileExtension = ".pdf.bin";

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

        if (!IsValidDataDirectory(pdfDataDir))
        {
            return pdfDataList;
        }

        logger.LogInformation("Loading PDF data...");

        LoadPdfDataFiles(pdfDataDir, pdfDataList);
        LoadPaperMetadataIfProvided(paperMetadataDir);

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

        LoadPaperFiles(metadataDir, papers);

        logger.LogInformation("Loaded {Count} papers", papers.Count);
        return papers;
    }

    /// <summary>
    /// Load a single PDF data file by sanitized DOI
    /// </summary>
    public PdfData? LoadPdfData(string pdfDataDir, string sanitizedDoi)
    {
        try
        {
            var filePath = Path.Combine(pdfDataDir, $"{sanitizedDoi}{PdfBinaryFileExtension}");

            if (!File.Exists(filePath))
            {
                logger.LogWarning("PDF data file not found: {FilePath}", filePath);
                return null;
            }

            var pdfData = DeserializeBinaryFile<PdfData>(filePath);

            if (pdfData != null)
            {
                logger.LogDebug("Loaded PDF data for DOI: {SanitizedDoi}", sanitizedDoi);
            }

            return pdfData;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Error loading PDF data for DOI {SanitizedDoi}: {Error}",
                sanitizedDoi,
                ex.Message
            );
            return null;
        }
    }

    private static bool IsValidDataDirectory(DirectoryInfo directory)
    {
        return directory.Exists && directory.GetFiles(BinaryFileExtension).Length > 0;
    }

    private void LoadPdfDataFiles(DirectoryInfo directory, List<PdfData> pdfDataList)
    {
        foreach (var file in directory.GetFiles(BinaryFileExtension))
        {
            var pdfData = DeserializeBinaryFile<PdfData>(file.FullName);
            if (pdfData != null)
            {
                pdfDataList.Add(pdfData);
            }
        }
    }

    private void LoadPaperFiles(DirectoryInfo directory, List<Paper> papers)
    {
        foreach (var file in directory.GetFiles(BinaryFileExtension))
        {
            var paper = DeserializeBinaryFile<Paper>(file.FullName);
            if (paper != null)
            {
                papers.Add(paper);
            }
        }
    }

    private void LoadPaperMetadataIfProvided(string? paperMetadataDir)
    {
        if (string.IsNullOrEmpty(paperMetadataDir))
            return;

        var papers = LoadPapersFromMetadata(paperMetadataDir);
        if (papers.Count > 0)
        {
            pdfDescriptionService.UpdatePaperCache(papers);
        }
    }

    private T? DeserializeBinaryFile<T>(string filePath)
        where T : class
    {
        try
        {
            var bin = File.ReadAllBytes(filePath);
            return MemoryPackSerializer.Deserialize<T>(bin);
        }
        catch (Exception ex)
        {
            var fileName = Path.GetFileName(filePath);
            logger.LogWarning(
                "Error loading {Type} {FileName}: {Error}",
                typeof(T).Name,
                fileName,
                ex.Message
            );
            return null;
        }
    }
}
