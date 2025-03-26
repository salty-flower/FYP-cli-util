using System.Collections.Generic;

namespace DataCollection.Models.Export;

/// <summary>
/// Bug terminology analysis for a single paper
/// </summary>
public class PaperBugTerminologyAnalysis
{
    public required string Title { get; set; }
    public required string FileName { get; set; }
    public required string DOI { get; set; }
    public int TotalBugSentences { get; set; }
    public required List<BugSentence> BugSentences { get; set; }
    public required Dictionary<string, int> WordFrequency { get; set; }
}
