using System.ComponentModel.DataAnnotations;

namespace DataCollection.Infrastructure.Options;

public class PatternMatchingOptions
{
    public const string SectionName = "PatternMatching";

    [Range(10, 500)]
    public int ContextWindowSize { get; set; } = 100;

    public Dictionary<string, double> ConfidenceScores { get; set; } =
        new()
        {
            { "PdfAnalysis", 0.9 },
            { "KeywordAnalysis", 0.95 },
            { "RepositoryAnalysis", 0.85 },
            { "WebSearch", 0.8 },
            { "Default", 0.7 },
        };

    public List<ConfidenceBoost> ConfidenceBoosts { get; set; } =
        new()
        {
            new() { MatchCountThreshold = 50, BoostValue = 0.1 },
            new() { MatchCountThreshold = 10, BoostValue = 0.05 },
            new() { MatchCountThreshold = 0, BoostValue = 0.0 },
        };

    public List<string> UrlCleaningSuffixes { get; set; } =
        new() { ".", ",", ";", ")", "]", "}", " ", "\t", "\n", "\r" };

    [Range(0, 60)]
    public int CacheExpirationMinutes { get; set; } = 5;
}

public class ConfidenceBoost
{
    [Range(0, 1000)]
    public int MatchCountThreshold { get; set; }

    [Range(0.0, 1.0)]
    public double BoostValue { get; set; }
}
