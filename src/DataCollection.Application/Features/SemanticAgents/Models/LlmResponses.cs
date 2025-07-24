namespace DataCollection.Application.Features.SemanticAgents.Models;

public record LlmRepositoryVerificationResponse
{
    public string Relevance { get; init; } = "NOT_RELEVANT";

    public double Confidence { get; init; }

    public string Justification { get; init; } = string.Empty;
}
