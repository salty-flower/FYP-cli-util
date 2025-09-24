using System.Text.Json;
using System.Text.Json.Serialization;
using DataCollection.Core.Models;
using DataCollection.Core.Models.IssueTracker.Responses;
using DataCollection.Infrastructure.Models.BugList;

namespace DataCollection.Infrastructure.Serialization;

[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(MatchObject[][]))]
[JsonSerializable(typeof(IssueAnalysisResponse))]
[JsonSerializable(typeof(SubjectiveIssueAnalysis))]
[JsonSerializable(typeof(BugListDiscoveryAnalysis))]
[JsonSerializable(typeof(JsonElement[]))]
[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
public partial class AppJsonContext : JsonSerializerContext;
