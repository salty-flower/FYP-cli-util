using System.Collections.Generic;

namespace DataCollection.Models.Export;

public class PdfEvaluationItem
{
    public required string PdfName { get; set; }

    public required string Filename { get; set; }

    public required Dictionary<string, int> KeywordCounts { get; set; }
}
