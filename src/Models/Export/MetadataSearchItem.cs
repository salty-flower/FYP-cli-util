namespace DataCollection.Models.Export;

public class MetadataSearchItem
{
    public required PaperReference Paper { get; set; }

    public required string Match { get; set; }

    public required string Context { get; set; }
}
