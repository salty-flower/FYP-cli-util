using DataCollection.Application.Models.Export.BugAnalysis;
using DataCollection.Application.Models.Export.PaperAnalysis;
using DataCollection.Core.Models;

namespace DataCollection.Application.Models.Commands;

public record ReconstructParagraphsCommand
{
    public required MatchObject[] PageLines { get; init; }
    public bool EnableHyphenHandling { get; init; } = true;
    public double ParagraphBreakThreshold { get; init; } = 5.0;
    public double IndentationThreshold { get; init; } = 10.0;
    public int MinLineLength { get; init; } = 3;
    public bool EnableBulletPointDetection { get; init; } = true;
}

public record CountKeywordsCommand
{
    public required string Text { get; init; }
    public required string[] Keywords { get; init; }
    public bool NormalizeKeywords { get; init; } = true;
    public bool CaseSensitive { get; init; } = false;
}

public record CountKeywordsInTextsCommand
{
    public required IEnumerable<string> Texts { get; init; }
    public required string[] Keywords { get; init; }
    public bool NormalizeKeywords { get; init; } = true;
    public bool CaseSensitive { get; init; } = false;
}

public record ExtractBugSentencesCommand
{
    public required PdfData PdfData { get; init; }
    public required string BugPattern { get; init; }
    public required PaperBugTerminologyAnalysis ExtractionResult { get; init; }
    public bool AdjectivesOnly { get; init; } = false;
    public int MinSentenceLength { get; init; } = 10;
    public int MaxSentenceLength { get; init; } = 500;
    public bool EnableSentenceFiltering { get; init; } = true;
}
