using System.Text.Json.Serialization;
using DataCollection.Models.Export;

namespace DataCollection.Commands;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(BugTablesSearchResult))]
[JsonSerializable(typeof(TechniquesSearchResult))]
[JsonSerializable(typeof(AnalysisOutput))]
public partial class ExportModelJsonContext : JsonSerializerContext { }
