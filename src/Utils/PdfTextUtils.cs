using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DataCollection.Models;
using DataCollection.Models.Export;
using DataCollection.Services;

namespace DataCollection.Utils;

/// <summary>
/// Utility class for PDF text processing operations
/// </summary>
public static class PdfTextUtils
{
    /// <summary>
    /// Process a page of lines into reconstructed paragraphs
    /// </summary>
    /// <param name="pageLines">Array of text line objects from a PDF page</param>
    /// <returns>List of reconstructed paragraph texts</returns>
    public static List<string> ReconstructParagraphs(MatchObject[] pageLines)
    {
        if (pageLines == null || pageLines.Length == 0)
            return new List<string>();

        // Group lines into paragraphs to handle line breaks properly
        var paragraphs = new List<string>();
        var currentParagraph = new List<string>();
        bool previousLineEndsWithHyphen = false;

        for (int lineIndex = 0; lineIndex < pageLines.Length; lineIndex++)
        {
            var lineObject = pageLines[lineIndex];
            if (lineObject == null || string.IsNullOrWhiteSpace(lineObject.Text))
            {
                // Empty line means end of paragraph
                if (currentParagraph.Count > 0)
                {
                    paragraphs.Add(
                        JoinParagraphWithHyphenHandling(
                            currentParagraph,
                            ref previousLineEndsWithHyphen
                        )
                    );
                    currentParagraph.Clear();
                    previousLineEndsWithHyphen = false;
                }
                continue;
            }

            string lineText = lineObject.Text.Trim();

            // Skip very short lines that are likely headers or page numbers
            if (lineText.Length < 3)
                continue;

            // Check for paragraph breaks based on indentation or other markers
            if (
                ShouldStartNewParagraph(lineObject, lineIndex > 0 ? pageLines[lineIndex - 1] : null)
            )
            {
                if (currentParagraph.Count > 0)
                {
                    paragraphs.Add(
                        JoinParagraphWithHyphenHandling(
                            currentParagraph,
                            ref previousLineEndsWithHyphen
                        )
                    );
                    currentParagraph.Clear();
                    previousLineEndsWithHyphen = false;
                }
            }

            // Add line to current paragraph
            currentParagraph.Add(lineText);

            // Update hyphen flag for next iteration
            previousLineEndsWithHyphen = lineText.EndsWith("-");
        }

        // Add the last paragraph if any
        if (currentParagraph.Count > 0)
        {
            paragraphs.Add(
                JoinParagraphWithHyphenHandling(currentParagraph, ref previousLineEndsWithHyphen)
            );
        }

        return paragraphs;
    }

    /// <summary>
    /// Join lines into a paragraph with proper handling of hyphenated words
    /// </summary>
    /// <param name="lines">Lines to join into a paragraph</param>
    /// <param name="previousLineEndsWithHyphen">Reference to track hyphenation state</param>
    /// <returns>Joined paragraph text</returns>
    public static string JoinParagraphWithHyphenHandling(
        List<string> lines,
        ref bool previousLineEndsWithHyphen
    )
    {
        var result = new StringBuilder();

        for (int i = 0; i < lines.Count; i++)
        {
            string currentLine = lines[i].Trim();

            if (i > 0)
            {
                string previousLine = lines[i - 1].Trim();

                if (previousLine.EndsWith("-"))
                {
                    // Handle hyphenated word continuation
                    // Remove the hyphen from the previous line and directly append current line
                    result.Length = result.Length - 1; // Remove the hyphen
                    result.Append(currentLine);
                }
                else
                {
                    // Add a space between lines within the same paragraph
                    result.Append(" ").Append(currentLine);
                }
            }
            else
            {
                result.Append(currentLine);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Determine if a new line should start a new paragraph based on PDF layout
    /// </summary>
    /// <param name="currentLine">Current line object</param>
    /// <param name="previousLine">Previous line object</param>
    /// <returns>True if a new paragraph should be started</returns>
    public static bool ShouldStartNewParagraph(MatchObject currentLine, MatchObject? previousLine)
    {
        if (previousLine == null)
            return false;

        // Check if there's significant spacing between lines (indicating a paragraph break)
        double verticalGap = currentLine.Top - previousLine.Bottom;

        // Check for indentation (first line of paragraphs often indented)
        bool isIndented = currentLine.X0 > previousLine.X0 + 10; // 10 pixels threshold

        // Check if line starts with a capital letter potentially indicating new sentence
        bool startsWithCapital =
            !string.IsNullOrEmpty(currentLine.Text) && char.IsUpper(currentLine.Text.First());

        // Check for bullet points or numbered lists
        bool isBulletPoint = Regex.IsMatch(currentLine.Text.TrimStart(), @"^[\•\-\*]|^\d+\.");

        // Consider it a new paragraph if there's significant vertical gap or indentation
        return verticalGap > 5 || isIndented || isBulletPoint;
    }

    /// <summary>
    /// Extract all bug-related sentences from a PDF with context awareness and adjective filtering
    /// </summary>
    /// <param name="pdfData">PDF data object</param>
    /// <param name="bugPattern">Regular expression pattern for detecting bug mentions</param>
    /// <param name="extractionResult">Object to store the extraction results</param>
    /// <param name="nlpService">Optional NLP service for adjective filtering</param>
    /// <param name="adjectivesOnly">Whether to filter for adjectives only</param>
    /// <returns>Total number of bug sentences found</returns>
    public static int ExtractBugSentences(
        PdfData pdfData,
        Regex bugPattern,
        PaperBugTerminologyAnalysis extractionResult,
        bool adjectivesOnly = false
    )
    {
        int totalBugSentences = 0;

        for (int pageIndex = 0; pageIndex < pdfData.TextLines.Length; pageIndex++)
        {
            var pageLines = pdfData.TextLines[pageIndex];
            if (pageLines == null)
                continue;

            // Reconstruct paragraphs from lines
            var paragraphs = ReconstructParagraphs(pageLines);

            // Process each paragraph
            for (int paragraphIndex = 0; paragraphIndex < paragraphs.Count; paragraphIndex++)
            {
                string paragraph = paragraphs[paragraphIndex];

                // Extract sentences from the paragraph
                var sentences = TextProcessingUtils.ExtractSentences(paragraph);

                foreach (var sentence in sentences)
                {
                    if (bugPattern.IsMatch(sentence))
                    {
                        List<string> words;
                        Dictionary<string, int> wordCounts;

                        // Extract words from the sentence
                        if (adjectivesOnly)
                        {
                            // Use NLP service to get adjectives only
                            wordCounts = AdjectiveAnalyzer.AnalyzeSentenceAdjectives(sentence);

                            // Extract the adjective words as a list
                            words = wordCounts.Keys.ToList();
                        }
                        else
                        {
                            // Standard word extraction without POS filtering
                            words = TextProcessingUtils.ExtractWords(sentence);
                            wordCounts = TextProcessingUtils.CountWords(words);
                        }

                        // Add to sentence-level data
                        var bugSentence = new BugSentence
                        {
                            Text = sentence,
                            Page = pageIndex + 1,
                            // Use paragraph index instead of line index
                            Line = paragraphIndex + 1,
                            WordCount = words.Count,
                            WordFrequency = wordCounts,
                        };

                        extractionResult.BugSentences.Add(bugSentence);
                        totalBugSentences++;

                        // Update paper-level word frequency
                        foreach (var word in wordCounts)
                        {
                            if (extractionResult.WordFrequency.ContainsKey(word.Key))
                                extractionResult.WordFrequency[word.Key] += word.Value;
                            else
                                extractionResult.WordFrequency[word.Key] = word.Value;
                        }
                    }
                }
            }
        }

        return totalBugSentences;
    }
}
