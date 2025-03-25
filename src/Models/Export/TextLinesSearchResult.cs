using System;
using System.Collections.Generic;

namespace DataCollection.Models.Export;

public class TextLinesSearchResult
{
    public required string Pattern { get; set; }

    public int TotalMatches { get; set; }

    public int PdfsWithMatches { get; set; }

    public DateTime Timestamp { get; set; }

    public required List<TextLinesPdfResult> Results { get; set; }
}
