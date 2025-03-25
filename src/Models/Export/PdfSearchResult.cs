using System;
using System.Collections.Generic;

namespace DataCollection.Models.Export;

public class PdfSearchResult
{
    public required string Pattern { get; set; }

    public int TotalMatches { get; set; }

    public DateTime Timestamp { get; set; }

    public required List<PdfSearchItem> Results { get; set; }
}
