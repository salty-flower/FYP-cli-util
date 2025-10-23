using System.Text.Json.Serialization;
using DataCollection.Application.Features.IssueAnalysis.Rules;

namespace DataCollection.Application.Serialization;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(IssueSubjectiveStatusNaiveBaselinePayload))]
public partial class IssueAnalysisJsonContext : JsonSerializerContext;
