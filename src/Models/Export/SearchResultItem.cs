namespace DataCollection.Models.Export;

public class SearchResultItem
{
    public int Page { get; set; }

    public int Line { get; set; }

    public required string Text { get; set; }

    public required BoundingBox BoundingBox { get; set; }
}
