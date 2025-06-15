using DataCollection.Infrastructure.Models.OpenAI;

namespace DataCollection.Application.Features.IssueAnalysis.Rules;

public class BatchRequest
{
    public required string CustomId { get; init; }
    public required string Method { get; init; }
    public required string Url { get; init; }
    public required ChatCompletionRequest Body { get; init; }
}
