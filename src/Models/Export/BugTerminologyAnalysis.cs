using System.Collections.Generic;

namespace DataCollection.Models.Export;

/// <summary>
/// Represents the full bug terminology analysis for multiple papers
/// </summary>
public class BugTerminologyAnalysis
{
    public required BugTerminologySummary Summary { get; set; }
    public required List<PaperBugTerminologyAnalysis> PaperAnalyses { get; set; }
    public required Dictionary<string, int> GlobalWordFrequency { get; set; }
}
