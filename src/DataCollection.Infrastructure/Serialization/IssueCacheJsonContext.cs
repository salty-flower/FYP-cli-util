using System.Text.Json.Serialization;
using DataCollection.Core.Models.IssueTracker;
using DataCollection.Infrastructure.Models.OpenAI;

namespace DataCollection.Infrastructure.Serialization;

[JsonSerializable(typeof(AnalysisResultModel))]
[JsonSerializable(typeof(CachedAnalysisResult))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower
)]
public partial class IssueCacheJsonContext : JsonSerializerContext { }
