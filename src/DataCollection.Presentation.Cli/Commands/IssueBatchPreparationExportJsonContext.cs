using System.Text.Json.Serialization;
using DataCollection.Application.Features.IssueProcessing;
using DataCollection.Core.Models.IssueTracker.Responses;
using DataCollection.Infrastructure.Models.OpenAI;

namespace DataCollection.Presentation.Cli.Commands;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower
)]
[JsonSerializable(typeof(IssueBatchPreparationRecord))]
[JsonSerializable(typeof(IssueBatchPreparationMetadata))]
[JsonSerializable(typeof(DeterministicIssueAnalysis))]
[JsonSerializable(typeof(ChatCompletionRequest))]
[JsonSerializable(typeof(ChatMessageModel))]
[JsonSerializable(typeof(ResponseFormatModel))]
[JsonSerializable(typeof(JsonSchemaModel))]
public partial class IssueBatchPreparationExportJsonContext : JsonSerializerContext;
