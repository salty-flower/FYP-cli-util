namespace DataCollection.Application.Options;

public class KeywordOptions
{
    public const string SectionName = "Keywords";

    public List<string> ArtifactKeywords { get; set; } =
        new()
        {
            "artifact",
            "benchmark",
            "dataset",
            "data",
            "evaluation",
            "experiment",
            "implementation",
            "tool",
            "prototype",
            "framework",
            "library",
            "system",
            "model",
            "algorithm",
            "source code",
            "code",
            "repository",
            "github",
            "replication",
            "reproduction",
            "validation",
            "verification",
            "testing",
            "case study",
            "empirical study",
            "user study",
            "survey",
            "analysis",
            "measurement",
            "metric",
            "performance",
            "comparison",
            "baseline",
            "ground truth",
            "gold standard",
            "reference",
            "standard",
            "specification",
            "documentation",
            "manual",
            "guide",
            "tutorial",
            "example",
            "demo",
            "sample",
            "template",
            "pattern",
            "best practice",
            "methodology",
            "approach",
            "technique",
            "strategy",
            "solution",
            "method",
        };

    public List<string> BugListKeywords { get; set; } =
        new()
        {
            "bug list",
            "bug database",
            "defect list",
            "issue list",
            "fault list",
            "error list",
            "failure list",
            "problem list",
            "anomaly list",
            "inconsistency list",
            "vulnerability list",
            "security issue",
            "known issues",
            "reported bugs",
        };
}
