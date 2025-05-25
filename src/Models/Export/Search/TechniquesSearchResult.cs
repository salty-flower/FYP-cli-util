using System.Collections.Generic;
using DataCollection.Models.Export.Results;

namespace DataCollection.Models.Export.Search;

public class TechniquesSearchResult
{
    public int TotalMatches { get; set; }

    public required List<TechniqueResult> Results { get; set; }
}
