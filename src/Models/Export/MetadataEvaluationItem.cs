using System.Collections.Generic;
using DataCollection.Models.Export.PaperAnalysis;

namespace DataCollection.Models.Export;

public class MetadataEvaluationItem
{
    public required PaperReference Paper { get; set; }

    public required Dictionary<string, int> KeywordCounts { get; set; }
}
