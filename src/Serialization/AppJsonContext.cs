using System.Text.Json.Serialization;
using DataCollection.Models.IssueTracker.Responses;
using DataCollection.Models.OpenAI;

namespace DataCollection.Serialization;

[JsonSerializable(typeof(IssueAnalysisResponse))]
[JsonSerializable(typeof(BatchJobResponse))]
[JsonSerializable(typeof(BatchResponse))]
[JsonSerializable(typeof(BatchResponseData))]
[JsonSerializable(typeof(BatchResponseBody))]
[JsonSerializable(typeof(BatchChoice))]
[JsonSerializable(typeof(BatchMessage))]
[JsonSerializable(typeof(BatchRequestCounts))]
[JsonSerializable(typeof(BatchRequestModel))]
[JsonSerializable(typeof(CreateBatchJobRequest))]
[JsonSerializable(typeof(ChatMessageModel))]
[JsonSerializable(typeof(ChatCompletionRequest))]
[JsonSerializable(typeof(ResponseFormatModel))]
[JsonSerializable(typeof(JsonSchemaModel))]
[JsonSerializable(typeof(object))] // For fallback cases
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true
)]
public partial class OpenAIBatchRequestJsonContext : JsonSerializerContext { }
