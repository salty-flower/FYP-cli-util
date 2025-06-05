namespace DataCollection.Infrastructure.Models.WebSearch;

public class WebSearchResult
{
    public required string Title { get; set; }

    public required string Url { get; set; }

    public required string Snippet { get; set; }
    public required string Source { get; set; }
}
