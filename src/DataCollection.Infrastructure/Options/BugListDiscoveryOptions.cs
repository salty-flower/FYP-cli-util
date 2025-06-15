using System.ComponentModel.DataAnnotations;

namespace DataCollection.Infrastructure.Options;

public class BugListDiscoveryOptions
{
    public const string SectionName = "BugListDiscovery";

    // Confidence thresholds
    [Range(0.0, 1.0)]
    public double PdfAnalysisConfidence { get; set; } = 0.9;

    [Range(0.0, 1.0)]
    public double KeywordAnalysisConfidence { get; set; } = 0.95;

    [Range(0.0, 1.0)]
    public double RepositoryAnalysisConfidence { get; set; } = 0.85;

    [Range(0.0, 2.0)]
    public double WebSearchConfidenceMultiplier { get; set; } = 0.8;

    [Range(0.0, 2.0)]
    public double LlmVerificationMultiplier { get; set; } = 1.2;

    [Range(0.0, 1.0)]
    public double HighConfidenceThreshold { get; set; } = 0.8;

    // Search and processing limits
    [Range(1, 20)]
    public int MaxSearchAttempts { get; set; } = 5;

    [Range(100, 10000)]
    public int RepositoryRetryDelayMs { get; set; } = 1000;

    [Range(50, 1000)]
    public int ContextWindowSize { get; set; } = 200;

    [Range(1000, 50000)]
    public int MaxTextLengthForLlm { get; set; } = 4000;

    // Display options
    [Range(1, 100)]
    public int MaxDisplayResults { get; set; } = 10;

    [Range(10, 200)]
    public int MaxTitleLength { get; set; } = 50;

    public int TitleTruncateLength => MaxTitleLength - 3;

    // File name patterns
    public string[] ReadmeFileNames { get; set; } =
        [
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
        ];

    // Bug-related keywords
    public string[] BugRelatedPaths { get; set; } =
        [
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
        ];

    // Artifact keywords for content analysis
    public string[] ArtifactKeywords { get; set; } =
        [
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
            "available at",
            "can be found",
            "accessible",
            "download",
            "obtain",
            "retrieve",
            "access",
            "provided",
            "supplement",
            "supplementary",
            "material",
            "materials",
            "resource",
            "resources",
            "code",
            "scripts",
            "files",
            "documentation",
            "manual",
            "guide",
            "tutorial",
            "readme",
            "license",
            "copyright",
            "open source",
            "free software",
        ];

    // Artifact section identifiers
    public string[] ArtifactSections { get; set; } =
        [
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
        ];

    // Known repository hosting services
    public string[] KnownRepositoryHosts { get; set; } =
        [
            "github.com",
            "gitlab.com",
            "bitbucket.org",
            "sourceforge.net",
            "code.google.com",
            "launchpad.net",
            "codeplex.com",
        ];

    // Stop words for search query generation
    public string[] SearchStopWords { get; set; } =
        [
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
        ];
}
