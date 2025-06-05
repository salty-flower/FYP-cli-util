using System.Text.Json.Serialization;
using DataCollection.Application.Models.Export.BugAnalysis;
using DataCollection.Application.Models.Export.PaperAnalysis;
using DataCollection.Application.Models.Export.Results;
using DataCollection.Application.Models.Export.Search;

namespace DataCollection.Application.Models.Export;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true
)]
[JsonSerializable(typeof(BugTablesSearchResult))]
[JsonSerializable(typeof(TechniquesSearchResult))]
[JsonSerializable(typeof(AnalysisOutput))]
[JsonSerializable(typeof(BugTerminologyAnalysis))]
[JsonSerializable(typeof(BugTerminologySummary))]
[JsonSerializable(typeof(PaperBugTerminologyAnalysis))]
[JsonSerializable(typeof(BugSentence))]
[JsonSerializable(typeof(BugListDiscoveryAnalysis))]
[JsonSerializable(typeof(BugListDiscoveryResult))]
[JsonSerializable(typeof(BugListSource))]
[JsonSerializable(typeof(ArtifactRepository))]
[JsonSerializable(typeof(BugListAnalysisRequest))]
[JsonSerializable(typeof(BugListAnalysisResponse))]
[JsonSerializable(typeof(ArtifactMention))]
[JsonSerializable(typeof(RepositoryVerificationRequest))]
[JsonSerializable(typeof(RepositoryVerificationResponse))]
[JsonSerializable(typeof(ArtifactVerificationRequest))]
[JsonSerializable(typeof(ArtifactVerificationResponse))]
[JsonSerializable(typeof(RepositoryBugFileAnalysisRequest))]
[JsonSerializable(typeof(RepositoryBugFileAnalysisResponse))]
[JsonSerializable(typeof(BugRelatedFile))]
public partial class ExportModelJsonContext : JsonSerializerContext { }
