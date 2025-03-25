namespace DataCollection.Models.Export;

public class PaperInfo
{
    public required string Title { get; set; }

    public required string Doi { get; set; }

    public required string[] Authors { get; set; }
}
