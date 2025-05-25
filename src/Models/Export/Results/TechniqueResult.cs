using DataCollection.Models.Export.PaperAnalysis;

namespace DataCollection.Models.Export.Results;

public class TechniqueResult
{
    public required PaperInfo Paper { get; set; }

    public required string Match { get; set; }

    public required string Context { get; set; }
}
