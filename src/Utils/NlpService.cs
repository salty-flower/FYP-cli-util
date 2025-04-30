using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DataCollection.Utils;
using Python.Runtime;

namespace DataCollection.Services;

public static partial class AdjectiveAnalyzer
{
    public static List<string> GetAdjectivesFromSentence(string text)
    {
        using (Py.GIL())
        {
            dynamic nltk = Py.Import("nltk");
            dynamic re = Py.Import("re");

            nltk.data.path.append(
                Path.Combine(Directory.GetParent(Runtime.PythonDLL!)!.FullName, "nltk_data")
            );

            dynamic tokens = nltk.word_tokenize(text.ToLower());
            dynamic taggedWords = nltk.pos_tag(tokens);

            // Convert Python list to C# list for processing
            List<string> adjectives = [];
            foreach (dynamic pair in taggedWords)
            {
                string word = pair[0].As<string>();
                string tag = pair[1].As<string>();

                // Extract adjectives (JJ, JJR, JJS)
                if (
                    tag.StartsWith("JJ")
                    && word.Length > 2
                    && LettersOnly().Matches(word).Count > 0
                )
                    adjectives.Add(word);
            }

            return adjectives;
        }
    }

    public static Dictionary<string, int> AnalyzeSentenceAdjectives(string text) =>
        GetAdjectivesFromSentence(text)
            .GroupBy(adj => adj)
            .Where(g => !TextProcessingUtils.IsStopword(g.Key))
            .ToDictionary(g => g.Key, g => g.Count());

    [GeneratedRegex("^[a-z]+$")]
    private static partial Regex LettersOnly();
}
