namespace DataCollection.Models.Export.Search;

public class PdfSearchItem
{
    public required string PdfName { get; set; }

    public required string Filename { get; set; }

    public int MatchCount { get; set; }

    public required List<PdfMatchContext> Context { get; set; }
}
