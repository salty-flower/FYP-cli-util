namespace DataCollection.Models.Export.Results;

public class PdfEvaluationResult
{
    public required string Expression { get; set; }

    public int TotalMatches { get; set; }

    public DateTime Timestamp { get; set; }

    public required List<PdfEvaluationItem> MatchingPdfs { get; set; }
}
