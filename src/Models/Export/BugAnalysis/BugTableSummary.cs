using System.Collections.Generic;

namespace DataCollection.Models.Export.BugAnalysis;

public class BugTableSummary
{
    public int Count { get; set; }

    public required List<string> Tables { get; set; }
}
