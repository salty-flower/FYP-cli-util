using System.Text.Json.Serialization;
using DataCollection.Models.Export;

namespace DataCollection.Commands.Repl;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(MetadataSearchResult))]
[JsonSerializable(typeof(MetadataEvaluationResult))]
[JsonSerializable(typeof(PdfSearchResult))]
[JsonSerializable(typeof(PdfEvaluationResult))]
[JsonSerializable(typeof(TextLinesSearchResult))]
[JsonSerializable(typeof(SearchResultsExport))]
[JsonSerializable(typeof(KeywordCountsExport))]
public partial class ReplJsonContext : JsonSerializerContext { }
