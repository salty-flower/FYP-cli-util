namespace DataCollection.Application.Models.Export.Results;

public class MetadataEvaluationResult
{
    public required string Expression { get; set; }

    public int TotalMatches { get; set; }

    public DateTime Timestamp { get; set; }

    public required List<MetadataEvaluationItem> MatchingPapers { get; set; }
}
