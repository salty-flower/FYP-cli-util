using System.Collections.Generic;

namespace DataCollection.Models.Export;

/// <summary>
/// Represents the full bug terminology analysis for multiple papers
/// </summary>
public class BugTerminologyAnalysis
{
    public BugTerminologySummary Summary { get; set; }
    public List<PaperBugTerminologyAnalysis> PaperAnalyses { get; set; }
    public Dictionary<string, int> GlobalWordFrequency { get; set; }
}

/// <summary>
/// Summary information about the bug terminology analysis
/// </summary>
public class BugTerminologySummary
{
    public int TotalPapers { get; set; }
    public int PapersWithBugs { get; set; }
    public int TotalBugSentences { get; set; }
    public string SearchPattern { get; set; }
    public string Timestamp { get; set; }
    public bool AdjectivesOnly { get; set; }
}

/// <summary>
/// Bug terminology analysis for a single paper
/// </summary>
public class PaperBugTerminologyAnalysis
{
    public string Title { get; set; }
    public string FileName { get; set; }
    public string DOI { get; set; }
    public int TotalBugSentences { get; set; }
    public List<BugSentence> BugSentences { get; set; }
    public Dictionary<string, int> WordFrequency { get; set; }
}

/// <summary>
/// Represents a sentence containing a bug reference
/// </summary>
public class BugSentence
{
    public string Text { get; set; }
    public int Page { get; set; }
    public int Line { get; set; }
    public int WordCount { get; set; }
    public Dictionary<string, int> WordFrequency { get; set; }
}
