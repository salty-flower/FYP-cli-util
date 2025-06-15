namespace DataCollection.Infrastructure.Options;

public record LLMOptions
{
    public string IssueOverallStatusModel { get; init; } = "o4-mini";
    public string PdfContentAnalysisModel { get; init; } = "o4-mini";
    public string RepositoryVerificationModel { get; init; } = "o4-mini";
    public string AgentPlanningModel { get; init; } = "o4-mini";
    public string AgentExecutionModel { get; init; } = "o4-mini";
    public string AgentReflectionModel { get; init; } = "o4-mini";
}
