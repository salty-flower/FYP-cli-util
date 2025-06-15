namespace DataCollection.Application.Options;

public class ArtifactSectionOptions
{
    public const string SectionName = "ArtifactSections";

    public List<string> ArtifactSectionIdentifiers { get; set; } =
        new()
        {
            "artifact",
            "data",
            "code",
            "implementation",
            "tool",
            "benchmark",
            "dataset",
            "evaluation",
            "experiment",
            "reproduction",
            "replication",
        };
}
