using System.Text.Json;
using System.Text.Json.Serialization;
using DataCollection.Commands;
using DataCollection.Models.IssueTracker;
using DataCollection.Models.OpenAI;

namespace DataCollection.Serialization;

[JsonSerializable(typeof(IssueAnalysisResponse))]
[JsonSerializable(typeof(IssueCommands.CachedAnalysisResult))]
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
[JsonSerializable(typeof(AnalysisResultModel))]
[JsonSerializable(typeof(object))] // For fallback cases
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
public partial class AppJsonContext : JsonSerializerContext { }
