namespace DataCollection.Models.Export;

/// <summary>
/// Summary information about the bug terminology analysis
/// </summary>
public class BugTerminologySummary
{
    public int TotalPapers { get; set; }
    public int PapersWithBugs { get; set; }
    public int TotalBugSentences { get; set; }
    public required string SearchPattern { get; set; }
    public required string Timestamp { get; set; }
    public bool AdjectivesOnly { get; set; }
}
