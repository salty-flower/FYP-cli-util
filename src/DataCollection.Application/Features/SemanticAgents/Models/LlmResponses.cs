using System.Text.Json.Serialization;

namespace DataCollection.Application.Features.SemanticAgents.Models;

public record LlmRepositoryVerificationResponse
{
    [JsonPropertyName("relevance")]
    public string Relevance { get; init; } = "NOT_RELEVANT";

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("justification")]
    public string Justification { get; init; } = string.Empty;
}
