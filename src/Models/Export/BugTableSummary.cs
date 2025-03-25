using System.Collections.Generic;

namespace DataCollection.Models.Export;

public class BugTableSummary
{
    public int Count { get; set; }

    public required List<string> Tables { get; set; }
}
