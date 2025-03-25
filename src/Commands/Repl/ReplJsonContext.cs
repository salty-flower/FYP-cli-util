using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using DataCollection.Models;

namespace DataCollection.Commands.Repl;

#region Metadata REPL Models

public class MetadataSearchResult
{
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; }

    [JsonPropertyName("totalMatches")]
    public int TotalMatches { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("results")]
    public List<MetadataSearchItem> Results { get; set; }
}

public class MetadataSearchItem
{
    [JsonPropertyName("paper")]
    public PaperReference Paper { get; set; }

    [JsonPropertyName("match")]
    public string Match { get; set; }

    [JsonPropertyName("context")]
    public string Context { get; set; }
}

public class MetadataEvaluationResult
{
    [JsonPropertyName("expression")]
    public string Expression { get; set; }

    [JsonPropertyName("totalMatches")]
    public int TotalMatches { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("matchingPapers")]
    public List<MetadataEvaluationItem> MatchingPapers { get; set; }
}

public class MetadataEvaluationItem
{
    [JsonPropertyName("paper")]
    public PaperReference Paper { get; set; }

    [JsonPropertyName("keywordCounts")]
    public Dictionary<string, int> KeywordCounts { get; set; }
}

public class PaperReference
{
    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("doi")]
    public string DOI { get; set; }

    [JsonPropertyName("authors")]
    public string[] Authors { get; set; }
}

#endregion

#region PDF REPL Models

public class PdfSearchResult
{
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; }

    [JsonPropertyName("totalMatches")]
    public int TotalMatches { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("results")]
    public List<PdfSearchItem> Results { get; set; }
}

public class PdfSearchItem
{
    [JsonPropertyName("pdfName")]
    public string PdfName { get; set; }

    [JsonPropertyName("filename")]
    public string Filename { get; set; }

    [JsonPropertyName("matchCount")]
    public int MatchCount { get; set; }

    [JsonPropertyName("context")]
    public List<PdfMatchContext> Context { get; set; }
}

public class PdfMatchContext
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("match")]
    public string Match { get; set; }

    [JsonPropertyName("context")]
    public string Context { get; set; }
}

public class PdfEvaluationResult
{
    [JsonPropertyName("expression")]
    public string Expression { get; set; }

    [JsonPropertyName("totalMatches")]
    public int TotalMatches { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("matchingPdfs")]
    public List<PdfEvaluationItem> MatchingPdfs { get; set; }
}

public class PdfEvaluationItem
{
    [JsonPropertyName("pdfName")]
    public string PdfName { get; set; }

    [JsonPropertyName("filename")]
    public string Filename { get; set; }

    [JsonPropertyName("keywordCounts")]
    public Dictionary<string, int> KeywordCounts { get; set; }
}

#endregion

#region TextLines REPL Models

public class TextLinesSearchResult
{
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; }

    [JsonPropertyName("totalMatches")]
    public int TotalMatches { get; set; }

    [JsonPropertyName("pdfsWithMatches")]
    public int PdfsWithMatches { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("results")]
    public List<TextLinesPdfResult> Results { get; set; }
}

public class TextLinesPdfResult
{
    [JsonPropertyName("fileName")]
    public string FileName { get; set; }

    [JsonPropertyName("pdf")]
    public string Pdf { get; set; }

    [JsonPropertyName("resultCount")]
    public int ResultCount { get; set; }

    [JsonPropertyName("results")]
    public List<TextLinesResult> Results { get; set; }
}

public class TextLinesResult
{
    [JsonPropertyName("text")]
    public string Text { get; set; }
}

#endregion

#region Service Export Models

public class SearchResultsExport
{
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; }

    [JsonPropertyName("pdf")]
    public string Pdf { get; set; }

    [JsonPropertyName("resultCount")]
    public int ResultCount { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("results")]
    public List<SearchResultItem> Results { get; set; }
}

public class SearchResultItem
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("line")]
    public int Line { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; }

    [JsonPropertyName("boundingBox")]
    public BoundingBox BoundingBox { get; set; }
}

public class BoundingBox
{
    [JsonPropertyName("x0")]
    public float X0 { get; set; }

    [JsonPropertyName("top")]
    public float Top { get; set; }

    [JsonPropertyName("x1")]
    public float X1 { get; set; }

    [JsonPropertyName("bottom")]
    public float Bottom { get; set; }
}

public class KeywordCountsExport
{
    [JsonPropertyName("source")]
    public string Source { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("counts")]
    public Dictionary<string, int> Counts { get; set; }
}

#endregion

// JSON source generation context for REPL commands
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
public partial class ReplJsonContext : JsonSerializerContext
{
} 