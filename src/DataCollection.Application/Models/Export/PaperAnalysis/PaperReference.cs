namespace DataCollection.Application.Models.Export.PaperAnalysis;

public class PaperReference
{
    public required string Title { get; set; }

    public required string DOI { get; set; }

    public required string[] Authors { get; set; }
}
