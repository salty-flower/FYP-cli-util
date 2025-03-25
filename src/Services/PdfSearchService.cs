using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DataCollection.Models;

namespace DataCollection.Services;

/// <summary>
/// Service for searching PDF content
/// </summary>
public class PdfSearchService
{
    /// <summary>
    /// Default maximum number of matches to collect per PDF
    /// </summary>
    public const int DefaultMaxMatchesPerPdf = 500;

    /// <summary>
    /// Parse search pattern from command parts, handling quoted strings
    /// </summary>
    public static string ParseSearchPattern(string[] parts)
    {
        if (parts.Length < 2)
            return string.Empty;

        string pattern = parts[1];
        if (pattern.StartsWith("\"") && parts.Length > 2)
        {
            // Reconstruct quoted search term
            var searchTermParts = new List<string> { parts[1] };
            int i = 2;
            while (i < parts.Length && !parts[i - 1].EndsWith("\""))
            {
                searchTermParts.Add(parts[i]);
                i++;
            }
            pattern = string.Join(" ", searchTermParts);

            // Remove quotes
            if (pattern.StartsWith("\"") && pattern.EndsWith("\""))
            {
                pattern = pattern.Substring(1, pattern.Length - 2);
            }
        }

        return pattern;
    }

    /// <summary>
    /// Search for a pattern in a specific page of a PDF
    /// </summary>
    public static void SearchInPage(
        PdfData pdfData,
        Regex regex,
        int pageNum,
        List<(int PageNum, int LineNum, MatchObject Line)> results,
        int maxMatches = DefaultMaxMatchesPerPdf
    )
    {
        // Check for valid page index
        if (
            pdfData == null
            || pdfData.TextLines == null
            || pageNum < 0
            || pageNum >= pdfData.TextLines.Length
        )
        {
            return;
        }

        var lines = pdfData.TextLines[pageNum];

        // Check for null lines array
        if (lines == null)
        {
            return;
        }

        // Stop collecting if we've reached the maximum matches
        if (results.Count >= maxMatches)
        {
            return;
        }

        for (int j = 0; j < lines.Length; j++)
        {
            // Skip null lines
            if (lines[j] == null || string.IsNullOrEmpty(lines[j].Text))
            {
                continue;
            }

            try
            {
                if (regex.IsMatch(lines[j].Text))
                {
                    results.Add((pageNum, j, lines[j]));

                    // Stop if we've reached the limit
                    if (results.Count >= maxMatches)
                    {
                        return;
                    }
                }
            }
            catch (Exception)
            {
                // Silently skip lines that cause regex match errors
                continue;
            }
        }
    }

    /// <summary>
    /// Search in all pages of a PDF
    /// </summary>
    public List<(int PageNum, int LineNum, MatchObject Line)> SearchInPdf(
        PdfData pdfData,
        Regex regex,
        int? specificPage = null,
        int maxMatches = DefaultMaxMatchesPerPdf
    )
    {
        var results = new List<(int PageNum, int LineNum, MatchObject Line)>();

        if (pdfData == null || pdfData.TextLines == null)
            return results;

        // Search in specified page or all pages
        if (specificPage.HasValue)
        {
            int pageNum = specificPage.Value;
            if (pageNum >= 0 && pageNum < pdfData.TextLines.Length)
            {
                SearchInPage(pdfData, regex, pageNum, results, maxMatches);
            }
        }
        else
        {
            for (int i = 0; i < pdfData.TextLines.Length; i++)
            {
                SearchInPage(pdfData, regex, i, results, maxMatches);

                // Stop if we've reached the limit
                if (results.Count >= maxMatches)
                {
                    break;
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Search across all PDFs
    /// </summary>
    public Dictionary<PdfData, List<(int PageNum, int LineNum, MatchObject Line)>> SearchAllPdfs(
        List<PdfData> allPdfData,
        Regex regex,
        int maxMatchesPerPdf = DefaultMaxMatchesPerPdf,
        int maxPdfs = int.MaxValue
    )
    {
        var results = new Dictionary<PdfData, List<(int PageNum, int LineNum, MatchObject Line)>>();
        int pdfsWithMatches = 0;

        foreach (var pdf in allPdfData)
        {
            if (pdf == null || pdf.TextLines == null)
                continue;

            try
            {
                var pdfResults = SearchInPdf(pdf, regex, null, maxMatchesPerPdf);

                if (pdfResults.Count > 0)
                {
                    results[pdf] = pdfResults;
                    pdfsWithMatches++;

                    if (pdfsWithMatches >= maxPdfs)
                        break;
                }
            }
            catch (Exception)
            {
                // Skip PDFs that cause errors
                continue;
            }
        }

        return results;
    }
}
