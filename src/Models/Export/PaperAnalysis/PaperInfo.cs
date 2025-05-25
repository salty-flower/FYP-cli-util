namespace DataCollection.Models.Export.PaperAnalysis;

public class PaperInfo
{
    public required string Title { get; set; }

    public required string Doi { get; set; }

    public required string[] Authors { get; set; }
}
