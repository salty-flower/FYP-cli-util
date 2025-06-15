namespace DataCollection.Application.Features.BugDiscovery;

/// <summary>
/// Constants for bug list discovery operations
/// </summary>
public static class BugDiscoveryConstants
{
    // Confidence thresholds
    public const double PdfAnalysisConfidence = 0.9;
    public const double KeywordAnalysisConfidence = 0.95;
    public const double RepositoryAnalysisConfidence = 0.85;
    public const double WebSearchConfidenceMultiplier = 0.8;
    public const double LlmVerificationMultiplier = 1.2;
    public const double HighConfidenceThreshold = 0.8;

    // Search and processing limits
    public const int MaxSearchAttempts = 5;
    public const int RepositoryRetryDelayMs = 1000;
    public const int ContextWindowSize = 200;
    public const int MaxTextLengthForLlm = 4000;

    public static readonly string[] GitHubUrlPatterns =
    [
        @"https?://github\.com/([\w\-\.]+)/([\w\-\.]+)",
        @"https?://www\.github\.com/([\w\-\.]+)/([\w\-\.]+)",
        @"github\.com/([\w\-\.]+)/([\w\-\.]+)",
        @"www\.github\.com/([\w\-\.]+)/([\w\-\.]+)",
    ];

    // File name patterns
    public static readonly string[] ReadmeFileNames =
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
    public static readonly string[] BugRelatedPaths =
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
    public static readonly string[] ArtifactKeywords =
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
    public static readonly string[] ArtifactSections =
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
    public static readonly string[] KnownRepositoryHosts =
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
    public static readonly string[] SearchStopWords =
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
