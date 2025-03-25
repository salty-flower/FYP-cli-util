using System;
using System.Collections.Generic;

namespace DataCollection.Models.Export;

public class MetadataSearchResult
{
    public required string Pattern { get; set; }

    public int TotalMatches { get; set; }

    public DateTime Timestamp { get; set; }

    public required List<MetadataSearchItem> Results { get; set; }
}
