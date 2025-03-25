namespace DataCollection.Models.Export;

public class PdfMatchContext
{
    public int Page { get; set; }

    public required string Match { get; set; }

    public required string Context { get; set; }
}
