using System.Collections.Generic;

namespace DataCollection.Models.Export;

public class PaperAnalysisResult
{
    public required string Title { get; set; }
    public required string Doi { get; set; }
    public required List<string> Authors { get; set; }
    public required string Technique { get; set; }
    public required string TechniqueContext { get; set; }
    public required BugTableSummary BugTables { get; set; }
}
