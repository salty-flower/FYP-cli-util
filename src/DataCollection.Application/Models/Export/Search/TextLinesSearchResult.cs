using DataCollection.Application.Models.Export.Results;

namespace DataCollection.Application.Models.Export.Search;

public class TextLinesSearchResult
{
    public required string Pattern { get; set; }

    public int TotalMatches { get; set; }

    public int PdfsWithMatches { get; set; }

    public DateTime Timestamp { get; set; }

    public required List<TextLinesPdfResult> Results { get; set; }
}
