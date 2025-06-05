using DataCollection.Models.Export.BugAnalysis;

namespace DataCollection.Models.Export.PaperAnalysis;

public class MergedPaperAnalysis
{
    public required string Title { get; set; }

    public required string Doi { get; set; }

    public required List<string> Authors { get; set; }

    public required List<string> Techniques { get; set; }

    public required List<TechniqueContext> TechniqueContexts { get; set; }

    public required BugTableSummary BugTables { get; set; }
}
