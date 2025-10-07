using DataCollection.Core.Models.IssueTracker.Responses;
using DataCollection.Infrastructure.Models.OpenAI;

namespace DataCollection.Application.Features.IssueProcessing;

public record IssueBatchPreparationRecord(
    string CustomId,
    string Owner,
    string Repo,
    long IssueNumber,
    DeterministicIssueAnalysis Deterministic,
    ChatCompletionRequest Request
);
