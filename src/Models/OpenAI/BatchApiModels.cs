using System.Text.Json.Nodes;

namespace DataCollection.Models.OpenAI;

public record BatchRequestModel(string CustomId, string Method, string Url, object Body);

public record CreateBatchJobRequest(string InputFileId, string Endpoint, string CompletionWindow);

public record ChatMessageModel(string Role, string Content);

public record ChatCompletionRequest(
    string Model,
    ChatMessageModel[] Messages,
    ResponseFormatModel ResponseFormat
);

public record ResponseFormatModel(string Type, JsonSchemaModel JsonSchema);

public record JsonSchemaModel(string Name, JsonNode Schema, bool Strict);

public record AnalysisResultModel(
    string Owner,
    string Repository,
    long IssueNumber,
    string Status,
    string StatusDescription,
    object Analysis
);

public class BatchJobResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public string? OutputFileId { get; set; }
    public string? ErrorFileId { get; set; }
    public long? CreatedAt { get; set; }
    public long? InProgressAt { get; set; }
    public long? ExpiresAt { get; set; }
    public long? FinalizingAt { get; set; }
    public long? CompletedAt { get; set; }
    public long? FailedAt { get; set; }
    public long? ExpiredAt { get; set; }
    public long? CancellingAt { get; set; }
    public long? CancelledAt { get; set; }
    public BatchRequestCounts? RequestCounts { get; set; }
    public object? Metadata { get; set; }
}

public class BatchRequestCounts
{
    public int Total { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }
}

public class BatchResponse
{
    public string? CustomId { get; set; }
    public BatchResponseData? Response { get; set; }
}

public class BatchResponseData
{
    public BatchResponseBody? Body { get; set; }
}

public class BatchResponseBody
{
    public BatchChoice[]? Choices { get; set; }
}

public class BatchChoice
{
    public BatchMessage? Message { get; set; }
}

public class BatchMessage
{
    public string? Content { get; set; }
}
