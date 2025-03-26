using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DataCollection.Utils;

/// <summary>
/// Utility class for text processing operations used in analysis
/// </summary>
public static class TextProcessingUtils
{
    /// <summary>
    /// Extract sentences from text, handling academic text conventions
    /// </summary>
    /// <param name="text">The text to extract sentences from</param>
    /// <returns>List of sentences</returns>
    public static List<string> ExtractSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        // Academic papers often have abbreviations with periods (e.g., "et al.", "i.e.", "e.g.")
        // and citations that can confuse simple sentence splitting

        // First, temporarily replace common abbreviations to avoid splitting them
        var preprocessed = text.Replace("et al.", "et_al_TEMP")
            .Replace("i.e.", "ie_TEMP")
            .Replace("e.g.", "eg_TEMP")
            .Replace("vs.", "vs_TEMP")
            .Replace("cf.", "cf_TEMP")
            .Replace("pp.", "pp_TEMP")
            .Replace("Fig.", "Fig_TEMP")
            .Replace("Eq.", "Eq_TEMP")
            .Replace("etc.", "etc_TEMP")
            .Replace("Ph.D.", "PhD_TEMP")
            .Replace("M.Sc.", "MSc_TEMP")
            .Replace("B.Sc.", "BSc_TEMP");

        // Handle numbered references like [1], [2], etc. which often appear at the end of sentences
        preprocessed = Regex.Replace(preprocessed, @"\[\d+\]\.", "$0_TEMP");

        // Handle citations like (Smith et al. 2021) to prevent splitting on the period
        preprocessed = Regex.Replace(
            preprocessed,
            @"\([^)]*?\.\s*\d{4}\)",
            match => match.Value.Replace(".", "_DOT_")
        );

        // Now split the text into sentences
        var sentenceSplits = Regex.Split(preprocessed, @"(?<=[.!?])\s+(?=[A-Z])");

        // Process the splits and restore temporary replacements
        var sentences = sentenceSplits
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => RestoreAbbreviations(s))
            .ToList();

        return sentences;
    }

    /// <summary>
    /// Restore abbreviations that were temporarily replaced during sentence extraction
    /// </summary>
    private static string RestoreAbbreviations(string text)
    {
        // Restore all abbreviations
        return text.Replace("et_al_TEMP", "et al.")
            .Replace("ie_TEMP", "i.e.")
            .Replace("eg_TEMP", "e.g.")
            .Replace("vs_TEMP", "vs.")
            .Replace("cf_TEMP", "cf.")
            .Replace("pp_TEMP", "pp.")
            .Replace("Fig_TEMP", "Fig.")
            .Replace("Eq_TEMP", "Eq.")
            .Replace("etc_TEMP", "etc.")
            .Replace("PhD_TEMP", "Ph.D.")
            .Replace("MSc_TEMP", "M.Sc.")
            .Replace("BSc_TEMP", "B.Sc.")
            .Replace("_TEMP", ".")
            .Replace("_DOT_", ".");
    }

    /// <summary>
    /// Extract meaningful words from a sentence, filtering out stopwords and unimportant terms
    /// </summary>
    /// <param name="sentence">The sentence to extract words from</param>
    /// <returns>List of meaningful words</returns>
    public static List<string> ExtractWords(string sentence)
    {
        // First, normalize the text (lowercase, etc.)
        var normalized = sentence.ToLowerInvariant();

        // Extract individual words, handling special cases
        var words = new List<string>();

        // Match regular words, compound words with hyphens, and technical terms
        var matches = Regex.Matches(normalized, @"\b[a-z0-9]+-*[a-z0-9]+(?:-[a-z0-9]+)*\b");

        foreach (Match match in matches)
        {
            string word = match.Value;

            // Skip if it's a number or very short word
            if (Regex.IsMatch(word, @"^\d+$") || word.Length < 2)
                continue;

            // Skip common stopwords to focus on meaningful terms
            if (IsStopword(word))
                continue;

            words.Add(word);
        }

        return words;
    }

    /// <summary>
    /// Determine if a word is a stopword that should be filtered out
    /// </summary>
    /// <param name="word">The word to check</param>
    /// <returns>True if the word is a stopword</returns>
    public static bool IsStopword(string word)
    {
        // Common English stopwords to filter out
        var stopwords = new HashSet<string>
        {
            "the",
            "and",
            "a",
            "to",
            "of",
            "in",
            "is",
            "that",
            "it",
            "with",
            "for",
            "as",
            "on",
            "was",
            "be",
            "by",
            "this",
            "an",
            "which",
            "or",
            "from",
            "are",
            "we",
            "they",
            "can",
            "at",
            "have",
            "has",
            "had",
            "not",
            "but",
            "were",
            "their",
            "been",
            "would",
            "will",
            "when",
            "what",
            "who",
            "how",
            "all",
            "if",
            "may",
            "more",
            "no",
            "our",
            "one",
            "other",
            "some",
            "such",
            "than",
            "then",
            "there",
            "these",
            "them",
            "those",
            "its",
            "his",
            "her",
            "he",
            "she",
            "you",
            "your",
            "my",
            "do",
            "does",
            "did",
            "done",
        };

        return stopwords.Contains(word);
    }

    /// <summary>
    /// Count word frequencies in a list of words
    /// </summary>
    /// <param name="words">List of words to count</param>
    /// <returns>Dictionary of word frequencies</returns>
    public static Dictionary<string, int> CountWords(List<string> words)
    {
        // Count frequency of each word
        var wordCounts = new Dictionary<string, int>();
        foreach (var word in words)
        {
            if (wordCounts.ContainsKey(word))
                wordCounts[word]++;
            else
                wordCounts[word] = 1;
        }
        return wordCounts;
    }
}
