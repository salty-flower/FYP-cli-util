using System.Text.Json.Serialization;
using DataCollection.Application.Models.Export.Results;
using DataCollection.Application.Models.Export.Search;

namespace DataCollection.Application.Models.Export;

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
