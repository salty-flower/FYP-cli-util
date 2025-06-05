using DataCollection.Application.Models.Export.PaperAnalysis;

namespace DataCollection.Application.Models.Export;

public class MetadataEvaluationItem
{
    public required PaperReference Paper { get; set; }

    public required Dictionary<string, int> KeywordCounts { get; set; }
}
