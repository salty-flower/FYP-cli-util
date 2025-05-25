using System.Text.Json.Serialization;
using DataCollection.Models.IssueTracker;
using DataCollection.Models.OpenAI;

namespace DataCollection.Services;

[JsonSerializable(typeof(AnalysisResultModel))]
[JsonSerializable(typeof(CachedAnalysisResult))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower
)]
public partial class IssueCacheJsonContext : JsonSerializerContext { }
