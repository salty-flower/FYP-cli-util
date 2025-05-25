using System.Collections.Generic;

namespace DataCollection.Models.Export.BugAnalysis;

// Helper classes
public class BugTableInfo
{
    public required string Title { get; set; }
    public int TableCount { get; set; }
    public required List<string> Tables { get; set; }
}
