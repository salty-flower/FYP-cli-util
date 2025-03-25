using System;
using System.Collections.Generic;

namespace DataCollection.Models.Export;

public class SearchResultsExport
{
    public required string Pattern { get; set; }

    public required string Pdf { get; set; }

    public int ResultCount { get; set; }

    public DateTime Timestamp { get; set; }

    public required List<SearchResultItem> Results { get; set; }
}
