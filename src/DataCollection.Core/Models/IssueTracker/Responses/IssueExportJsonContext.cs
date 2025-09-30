using System.Text.Json.Serialization;

namespace DataCollection.Core.Models.IssueTracker.Responses;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(IssueAnalysisResult))]
[JsonSerializable(typeof(IssueAnalysisResponse))]
[JsonSerializable(typeof(DeterministicIssueAnalysis))]
[JsonSerializable(typeof(SubjectiveIssueAnalysis))]
public partial class IssueExportJsonContext : JsonSerializerContext { }
