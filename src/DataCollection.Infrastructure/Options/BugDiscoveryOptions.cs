using System.ComponentModel.DataAnnotations;

namespace DataCollection.Infrastructure.Options;

public class BugDiscoveryOptions
{
    public const string SectionName = "BugDiscovery";

    // Confidence thresholds
    [Range(0.1, 1.0)]
    public double PdfAnalysisConfidence { get; set; } = 0.9;

    [Range(0.1, 1.0)]
    public double KeywordAnalysisConfidence { get; set; } = 0.95;

    [Range(0.1, 1.0)]
    public double RepositoryAnalysisConfidence { get; set; } = 0.85;

    [Range(0.1, 1.0)]
    public double WebSearchConfidenceMultiplier { get; set; } = 0.8;

    [Range(0.1, 2.0)]
    public double LlmVerificationMultiplier { get; set; } = 1.2;

    [Range(0.1, 1.0)]
    public double HighConfidenceThreshold { get; set; } = 0.8;

    [Range(0.1, 1.0)]
    public double MinBugListConfidence { get; set; } = 0.7;

    [Range(0.1, 1.0)]
    public double MinRepositoryConfidence { get; set; } = 0.6;

    // Processing limits
    [Range(1, 20)]
    public int MaxSearchAttempts { get; set; } = 5;

    [Range(100, 10000)]
    public int RepositoryRetryDelayMs { get; set; } = 1000;

    [Range(50, 1000)]
    public int ContextWindowSize { get; set; } = 200;

    [Range(1000, 20000)]
    public int MaxTextLengthForLlm { get; set; } = 4000;

    [Range(1, 50)]
    public int MaxConcurrentOperations { get; set; } = 5;

    [Range(1, 100)]
    public int MaxResultsPerSearch { get; set; } = 10;

    // File patterns and keywords
    public List<string> ReadmeFileNames { get; set; } =
        new()
        {
            "README.md",
            "readme.md",
            "README.MD",
            "README.txt",
            "readme.txt",
            "README.rst",
            "readme.rst",
            "README",
            "readme",
            "Readme.md",
        };

    public List<string> BugRelatedPaths { get; set; } =
        new()
        {
            "issues",
            "bugs",
            "tickets",
            "defects",
            "problems",
            "errors",
            "failures",
            "exceptions",
            "crashes",
            "vulnerabilities",
        };

    public List<string> ArtifactKeywords { get; set; } =
        new()
        {
            "artifact",
            "repository",
            "repo",
            "source code",
            "implementation",
            "codebase",
            "project",
            "software",
            "program",
            "application",
            "system",
            "tool",
            "library",
            "framework",
            "dataset",
            "data",
            "benchmark",
            "evaluation",
            "experiment",
            "github",
            "gitlab",
            "bitbucket",
            "sourceforge",
        };

    public List<string> ArtifactSections { get; set; } =
        new()
        {
            "implementation",
            "code availability",
            "data availability",
            "supplementary material",
            "resources",
            "artifacts",
            "repository",
            "source code",
            "dataset",
            "benchmark",
            "evaluation",
            "experiment",
            "materials",
            "appendix",
        };

    public List<string> KnownRepositoryHosts { get; set; } =
        new()
        {
            "github.com",
            "gitlab.com",
            "bitbucket.org",
            "sourceforge.net",
            "code.google.com",
            "launchpad.net",
            "codeplex.com",
        };

    public List<string> SearchStopWords { get; set; } =
        new()
        {
            "a",
            "an",
            "the",
            "and",
            "or",
            "but",
            "in",
            "on",
            "at",
            "to",
            "for",
            "of",
            "with",
            "by",
            "using",
            "based",
            "approach",
            "method",
            "technique",
            "algorithm",
            "system",
            "framework",
        };

    // Feature flags
    public bool EnablePdfAnalysis { get; set; } = true;
    public bool EnableRepositoryAnalysis { get; set; } = true;
    public bool EnableWebSearch { get; set; } = true;
    public bool EnableLlmVerification { get; set; } = false;
    public bool EnableParallelProcessing { get; set; } = true;
    public bool EnableDetailedLogging { get; set; } = false;
    public bool EnableCaching { get; set; } = true;
}
