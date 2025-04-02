using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DataCollection.Models;

namespace DataCollection.Services;

/// <summary>
/// Service for handling PDF descriptions and metadata
/// </summary>
public class PdfDescriptionService
{
    private readonly List<Paper> _paperCache = new();

    /// <summary>
    /// Initialize with optional paper cache
    /// </summary>
    /// <param name="paperCache">Optional cache of papers for metadata lookup</param>
    public PdfDescriptionService(List<Paper>? paperCache = null)
    {
        if (paperCache != null)
        {
            _paperCache = paperCache;
        }
    }

    /// <summary>
    /// Get a description for an item (paper, PDF, etc.)
    /// </summary>
    public string GetItemDescription<T>(T item) =>
        item switch
        {
            Paper paper => $"{paper.Title} (DOI: {paper.Doi})",
            PdfData pdfData => GetEnhancedPdfDescription(pdfData),
            _ => item?.ToString() ?? "Unknown item",
        };

    /// <summary>
    /// Get an enhanced PDF description with metadata if available
    /// </summary>
    public string GetEnhancedPdfDescription(PdfData pdfData)
    {
        // First get the formatted filename
        string basicDescription = FormatPdfFileName(pdfData.FileName);

        // Now try to find matching paper metadata
        if (_paperCache.Count > 0)
        {
            // Extract DOI from filename if present
            string fileName = Path.GetFileNameWithoutExtension(pdfData.FileName);
            if (fileName.StartsWith("10.") && fileName.Contains('-'))
            {
                // This looks like a DOI-based filename
                string doi = fileName.Replace('-', '/');

                // Try to find matching paper by DOI
                var matchingPaper = _paperCache.FirstOrDefault(p =>
                    !string.IsNullOrEmpty(p.Doi)
                    && p.Doi.Equals(doi, StringComparison.OrdinalIgnoreCase)
                );

                if (matchingPaper != null)
                {
                    return $"{matchingPaper.Title} (DOI: {matchingPaper.Doi})";
                }
            }

            // If no DOI match, try by filename (some files might be named after the paper title)
            var possibleMatches = _paperCache
                .Where(p =>
                    !string.IsNullOrEmpty(p.Title)
                    && fileName.Contains(p.Title, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();

            if (possibleMatches.Count == 1)
            {
                return $"{possibleMatches[0].Title} (DOI: {possibleMatches[0].Doi})";
            }
        }

        // If no match found, return the basic description
        return basicDescription;
    }

    /// <summary>
    /// Format PDF filenames to be more readable
    /// </summary>
    public static string FormatPdfFileName(string fileName)
    {
        // Remove file extension
        string nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

        // Try to clean up DOI-based filenames (like 10.1145-3597503.3608128.pdf)
        if (nameWithoutExtension.Contains('-') && nameWithoutExtension.StartsWith("10."))
        {
            return $"Paper DOI: {nameWithoutExtension.Replace('-', '/')}";
        }

        return nameWithoutExtension;
    }

    /// <summary>
    /// Update the paper cache
    /// </summary>
    public void UpdatePaperCache(List<Paper> papers)
    {
        _paperCache.Clear();
        _paperCache.AddRange(papers);
    }

    /// <summary>
    /// Get the current paper cache
    /// </summary>
    public List<Paper> GetPaperCache() => _paperCache;
}
