using System.ComponentModel.DataAnnotations;

namespace DataCollection.Infrastructure.Options;

public class PdfAnalysisOptions
{
    public const string SectionName = "PdfAnalysis";

    [Range(1, 1000)]
    public int ContextWindowSize { get; set; } = 200;

    [Range(1, 20000)]
    public int MaxTextLengthForProcessing { get; set; } = 4000;

    [Range(1, 100)]
    public int MinSentenceLength { get; set; } = 10;

    [Range(1, 10000)]
    public int MaxSentenceLength { get; set; } = 500;

    [Range(1, 50)]
    public double ParagraphBreakThreshold { get; set; } = 5.0;

    [Range(1, 100)]
    public double IndentationThreshold { get; set; } = 10.0;

    [Range(1, 10)]
    public int MinLineLength { get; set; } = 3;

    public List<string> BugKeywords { get; set; } =
        new()
        {
            "bug",
            "bugs",
            "defect",
            "defects",
            "error",
            "errors",
            "fault",
            "faults",
            "failure",
            "failures",
            "issue",
            "issues",
            "problem",
            "problems",
        };

    public List<string> ExcludedPatterns { get; set; } =
        new()
        {
            @"^\d+$", // Pure numbers
            @"^[A-Z]{1,3}$", // Short uppercase codes
            @"^Page \d+", // Page numbers
            @"^Figure \d+", // Figure references
            @"^Table \d+", // Table references
            @"^References?$", // Reference sections
            @"^Bibliography$", // Bibliography sections
        };

    public bool EnableHyphenHandling { get; set; } = true;
    public bool EnableParagraphReconstruction { get; set; } = true;
    public bool EnableBulletPointDetection { get; set; } = true;
    public bool EnableKeywordNormalization { get; set; } = true;
    public bool EnableSentenceFiltering { get; set; } = true;
}
