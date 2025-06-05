using DataCollection.Application.Models.Export.PaperAnalysis;

namespace DataCollection.Application.Models.Export.Results;

public class TechniqueResult
{
    public required PaperInfo Paper { get; set; }

    public required string Match { get; set; }

    public required string Context { get; set; }
}
