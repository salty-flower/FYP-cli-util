using System.Collections.Generic;

namespace DataCollection.Models.Export;

public class BugTablePdfResult
{
    public required string FileName { get; set; }

    public required string Pdf { get; set; }

    public int ResultCount { get; set; }

    public required List<BugTableTextResult> Results { get; set; }
}
