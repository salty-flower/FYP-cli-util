using System.Collections.Generic;
using System.Linq;
using DataCollection.Utils;

namespace DataCollection;

public class PaperAnalyzer
{
    public Dictionary<string, int> CountKeywordsInText(string text, string[] keywords)
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

    public Dictionary<string, int> CountKeywordsInTexts(
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
}
