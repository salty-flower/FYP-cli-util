using System.Collections.Generic;

namespace DataCollection.Models.Export;

// Classes for deserialization
public class BugTablesSearchResult
{
    public int TotalMatches { get; set; }

    public int PdfsWithMatches { get; set; }

    public required List<BugTablePdfResult> Results { get; set; }
}
