using System.Collections.Generic;

namespace DataCollection.Models.Export;

/// <summary>
/// Represents a sentence containing a bug reference
/// </summary>
public class BugSentence
{
    public required string Text { get; set; }
    public int Page { get; set; }
    public int Line { get; set; }
    public int WordCount { get; set; }
    public required Dictionary<string, int> WordFrequency { get; set; }
}
