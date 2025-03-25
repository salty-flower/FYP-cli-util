namespace DataCollection.Models.Export;

public class AnalysisSummary
{
    public int TotalPapers { get; set; }

    public required string Timestamp { get; set; }

    public required string BugTablePattern { get; set; }

    public required string TechniquesPattern { get; set; }
}
