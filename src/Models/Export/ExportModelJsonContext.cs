using System.Text.Json.Serialization;
using DataCollection.Models.Export.BugAnalysis;
using DataCollection.Models.Export.PaperAnalysis;
using DataCollection.Models.Export.Results;
using DataCollection.Models.Export.Search;

namespace DataCollection.Commands;

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
public partial class ExportModelJsonContext : JsonSerializerContext { }
