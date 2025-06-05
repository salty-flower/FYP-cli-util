using DataCollection.Models.Export.PaperAnalysis;

namespace DataCollection.Models.Export.Results;

// Output classes
public class AnalysisOutput
{
    public required AnalysisSummary Summary { get; set; }

    public required List<MergedPaperAnalysis> Papers { get; set; }
}
