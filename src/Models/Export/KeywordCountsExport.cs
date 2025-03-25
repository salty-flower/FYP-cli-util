using System;
using System.Collections.Generic;

namespace DataCollection.Models.Export;

public class KeywordCountsExport
{
    public required string Source { get; set; }

    public DateTime Timestamp { get; set; }

    public required Dictionary<string, int> Counts { get; set; }
}
