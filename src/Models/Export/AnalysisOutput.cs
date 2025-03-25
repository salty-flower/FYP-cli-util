using System.Collections.Generic;

namespace DataCollection.Models.Export;

// Output classes
public class AnalysisOutput
{
    public required AnalysisSummary Summary { get; set; }

    public required List<MergedPaperAnalysis> Papers { get; set; }
}
