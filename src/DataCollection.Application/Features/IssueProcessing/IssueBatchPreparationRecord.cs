using DataCollection.Application.Features.IssueAnalysis.Rules;
using DataCollection.Core.Models.IssueTracker.Responses;
using DataCollection.Infrastructure.Models.OpenAI;

namespace DataCollection.Application.Features.IssueProcessing;

public record IssueBatchPreparationRecord : BatchRequestModel
{
    public IssueBatchPreparationRecord(
        string CustomId,
        string Method,
        string Url,
        object Body,
        IssueBatchPreparationMetadata Metadata
    ) : base(CustomId, Method, Url, Body)
    {
        this.Metadata = Metadata;
    }

    public IssueBatchPreparationRecord(
        BatchRequest Request,
        IssueBatchPreparationMetadata Metadata
    ) : this(Request.CustomId, Request.Method, Request.Url, Request.Body, Metadata)
    {
    }

    public IssueBatchPreparationMetadata Metadata { get; init; }
}

public record IssueBatchPreparationMetadata(
    string Owner,
    string Repo,
    long IssueNumber,
    DeterministicIssueAnalysis Deterministic
);
