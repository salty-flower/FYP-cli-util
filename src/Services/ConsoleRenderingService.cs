using System;
using System.Collections.Generic;
using System.Linq;
using DataCollection.Models;
using Spectre.Console;

namespace DataCollection.Services;

/// <summary>
/// Service for handling console rendering and formatting
/// </summary>
public class ConsoleRenderingService(PdfDescriptionService pdfDescriptionService)
{
    /// <summary>
    /// Escape markup for safe display in Spectre.Console
    /// </summary>
    public static string SafeMarkup(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return Markup.Escape(text);
    }

    /// <summary>
    /// Display a list of PDFs in a table
    /// </summary>
    public void DisplayPdfList(List<PdfData> pdfs, string title = "Available PDFs")
    {
        var table = new Table();
        table.AddColumn("#");
        table.AddColumn("PDF Name");
        table.AddColumn("Pages");

        for (int i = 0; i < pdfs.Count; i++)
        {
            var pdf = pdfs[i];
            table.AddRow(
                (i + 1).ToString(),
                SafeMarkup(pdfDescriptionService.GetItemDescription(pdf)),
                pdf.TextLines.Length.ToString()
            );
        }

        AnsiConsole.Write(new Rule($"{title} ({pdfs.Count})").RuleStyle("blue"));
        AnsiConsole.Write(table);
    }

    /// <summary>
    /// Display PDF collection summary information
    /// </summary>
    public void DisplayPdfCollectionInfo(List<PdfData> allPdfData)
    {
        AnsiConsole.Write(new Rule("PDF Collection Summary").RuleStyle("blue"));

        // Count total pages
        int totalPages = 0;
        foreach (var pdf in allPdfData)
        {
            totalPages += pdf.TextLines.Length;
        }

        double avgPages = totalPages / (double)allPdfData.Count;

        var table = new Table();
        table.AddColumn("Metric");
        table.AddColumn("Value");

        table.AddRow("Total PDFs", allPdfData.Count.ToString());
        table.AddRow("Total Pages", totalPages.ToString());
        table.AddRow("Average Pages", avgPages.ToString("F1"));

        if (allPdfData.Count > 0)
        {
            var largest = allPdfData[0];
            var smallest = allPdfData[0];

            foreach (var pdf in allPdfData)
            {
                if (pdf.TextLines.Length > largest.TextLines.Length)
                    largest = pdf;

                if (pdf.TextLines.Length < smallest.TextLines.Length)
                    smallest = pdf;
            }

            table.AddRow(
                "Largest Document",
                $"{SafeMarkup(pdfDescriptionService.GetItemDescription(largest))} ({largest.TextLines.Length} pages)"
            );
            table.AddRow(
                "Smallest Document",
                $"{SafeMarkup(pdfDescriptionService.GetItemDescription(smallest))} ({smallest.TextLines.Length} pages)"
            );
        }

        AnsiConsole.Write(table);
    }

    /// <summary>
    /// Display detailed information about a single PDF
    /// </summary>
    public void DisplayPdfInfo(PdfData pdfData)
    {
        string safeTitle = SafeMarkup(pdfDescriptionService.GetItemDescription(pdfData));
        AnsiConsole.Write(new Rule($"Document Information: {safeTitle}").RuleStyle("blue"));

        var table = new Table();
        table.AddColumn("Page");
        table.AddColumn("Lines");

        // Display page statistics
        for (int i = 0; i < pdfData.TextLines.Length; i++)
        {
            table.AddRow((i + 1).ToString(), pdfData.TextLines[i].Length.ToString());
        }

        AnsiConsole.Write(table);
    }

    /// <summary>
    /// Display a single text line with its properties
    /// </summary>
    public void DisplayTextLine(int pageNum, int lineNum, MatchObject line)
    {
        AnsiConsole.Write(new Rule($"Page {pageNum + 1}, Line {lineNum + 1}").RuleStyle("blue"));

        if (line == null)
        {
            AnsiConsole.MarkupLine("[red]Line is null[/]");
            return;
        }

        var panel = new Panel(SafeMarkup(line.Text ?? "<null text>")).Header("Text").Expand();

        AnsiConsole.Write(panel);

        var table = new Table();
        table.AddColumn("Property");
        table.AddColumn("Value");

        table.AddRow("X0", line.X0.ToString());
        table.AddRow("Top", line.Top.ToString());
        table.AddRow("X1", line.X1.ToString());
        table.AddRow("Bottom", line.Bottom.ToString());

        if (line.Chars != null)
        {
            table.AddRow("Characters", line.Chars.Length.ToString());
        }

        AnsiConsole.Write(table);
    }

    /// <summary>
    /// Display a page of text lines
    /// </summary>
    public void DisplayPage(PdfData pdfData, int pageNum)
    {
        // Validate page number
        if (pageNum < 0 || pageNum >= pdfData.TextLines.Length)
        {
            AnsiConsole.MarkupLine(
                $"[red]Invalid page number.[/] Valid range: 1-{pdfData.TextLines.Length}"
            );
            return;
        }

        var lines = pdfData.TextLines[pageNum];

        AnsiConsole.Write(new Rule($"Page {pageNum + 1} ({lines.Length} lines)").RuleStyle("blue"));

        var table = new Table();
        table.AddColumn("Line");
        table.AddColumn("Text");

        for (int i = 0; i < lines.Length; i++)
        {
            var text = lines[i]?.Text ?? "<null>";
            table.AddRow((i + 1).ToString(), SafeMarkup(text));
        }

        AnsiConsole.Write(table);
    }

    /// <summary>
    /// Display search results consistently
    /// </summary>
    public void DisplaySearchResults(
        List<(int PageNum, int LineNum, MatchObject Line)> results,
        string pattern,
        string pdfName = null,
        int? maxToShow = null,
        bool showAll = false
    )
    {
        // Safety check for null results
        if (results == null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Search results are null");
            return;
        }

        string escapedPattern = SafeMarkup(pattern);

        if (results.Count == 0)
        {
            string escapedPdfInfo = pdfName != null ? $" in {pdfName}" : "";
            AnsiConsole.MarkupLine(
                $"[yellow]No matches found[/] for '{escapedPattern}'{escapedPdfInfo}"
            );
            return;
        }

        if (pdfName == null)
        {
            AnsiConsole.Write(
                new Rule($"Found {results.Count} matches for '{escapedPattern}'").RuleStyle("green")
            );
        }

        // Determine how many results to show
        int showCount = showAll
            ? results.Count
            : (maxToShow.HasValue ? Math.Min(maxToShow.Value, results.Count) : results.Count);

        // Filter out any null results before creating the table
        var validResults = results.Take(showCount).Where(r => r.Line != null).ToList();

        // If all results were null, show a message and return
        if (validResults.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Results contained only null lines[/]");
            return;
        }

        // Show the results
        var table = new Table();
        table.AddColumn("Page");
        table.AddColumn("Line");
        table.AddColumn("Text");

        foreach (var result in validResults)
        {
            try
            {
                // Safely extract values
                int pageIdx = result.PageNum;
                int line = result.LineNum;
                string text = result.Line?.Text ?? "<null text>";

                // Ensure text is never empty to avoid Spectre.Console errors
                if (string.IsNullOrEmpty(text))
                {
                    text = "<empty text>";
                }

                table.AddRow((pageIdx + 1).ToString(), (line + 1).ToString(), SafeMarkup(text));
            }
            catch (Exception ex)
            {
                // If any single result fails, log the error but continue with others
                table.AddRow(
                    "?",
                    "?",
                    $"[red]Error processing result: {SafeMarkup(ex.Message)}[/]"
                );
            }
        }

        AnsiConsole.Write(table);

        // Show count of additional results if limited and not showing all
        if (!showAll && maxToShow.HasValue && results.Count > maxToShow.Value)
        {
            AnsiConsole.MarkupLine(
                $"[grey]... and {results.Count - maxToShow.Value} more matches[/] (use 'showall true' to see all)"
            );
        }
    }
}
