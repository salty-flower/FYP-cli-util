using DataCollection.Application.Models.Export.Results;

namespace DataCollection.Application.Models.Export.Search;

public class TechniquesSearchResult
{
    public int TotalMatches { get; set; }

    public required List<TechniqueResult> Results { get; set; }
}
