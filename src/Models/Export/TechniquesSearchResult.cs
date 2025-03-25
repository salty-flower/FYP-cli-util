using System.Collections.Generic;

namespace DataCollection.Models.Export;

public class TechniquesSearchResult
{
    public int TotalMatches { get; set; }

    public required List<TechniqueResult> Results { get; set; }
}
