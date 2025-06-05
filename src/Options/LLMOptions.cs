namespace DataCollection.Options;

public record LLMOptions
{
    public string IssueOverallStatusModel { get; init; } = "o4-mini";
    public string PdfContentAnalysisModel { get; init; } = "o4-mini";
}
