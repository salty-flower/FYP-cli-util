using System;
using System.Collections.Generic;
using System.Linq;

namespace DataCollection.Utils;

public static class PaperAnalyzer
{
    public static Dictionary<string, int> CountKeywordsInText(string text, string[] keywords)
    {
        var result = new Dictionary<string, int>();
        var lowerText = text.ToLower();

        foreach (var keyword in keywords)
        {
            var count = lowerText.CountSubstring(keyword);
            result[keyword] = count;
        }

        return result;
    }

    public static Dictionary<string, int> CountKeywordsInTexts(
        IEnumerable<string> texts,
        string[] keywords
    )
    {
        var result = new Dictionary<string, int>();

        foreach (var keyword in keywords)
        {
            var count = texts.Sum(text => text.ToLower().CountSubstring(keyword));
            result[keyword] = count;
        }

        return result;
    }

    /// <summary>
    /// Count occurrences of a single keyword in text
    /// </summary>
    public static int CountKeywordInText(string text, string keyword)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(keyword))
            return 0;

        var normalizedText = text.ToLowerInvariant();
        var normalizedKeyword = keyword.ToLowerInvariant();

        int count = 0;
        int index = 0;

        while (
            (index = normalizedText.IndexOf(normalizedKeyword, index, StringComparison.Ordinal))
            != -1
        )
        {
            count++;
            index += normalizedKeyword.Length;
        }

        return count;
    }

    /// <summary>
    /// Count occurrences of a single keyword in multiple text segments
    /// </summary>
    public static int CountKeywordInTexts(IEnumerable<string> texts, string keyword)
    {
        if (texts == null)
            return 0;

        int totalCount = 0;

        foreach (var text in texts)
        {
            totalCount += CountKeywordInText(text, keyword);
        }

        return totalCount;
    }
}
