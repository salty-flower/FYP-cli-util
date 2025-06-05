using DataCollection.Application.Models.Export.PaperAnalysis;

namespace DataCollection.Application.Models.Export.Search;

public class MetadataSearchItem
{
    public required PaperReference Paper { get; set; }

    public required string Match { get; set; }

    public required string Context { get; set; }
}
