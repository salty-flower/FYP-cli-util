namespace DataCollection.Application.Models.Commands.PatternMatching;

public record FindMatchesCommand
{
    public required string Text { get; init; }
    public required string Category { get; init; }
    public int ContextWindow { get; init; } = 100;
}

public record DetermineUrlTypeCommand
{
    public required string Url { get; init; }
}

public record CalculateConfidenceCommand
{
    public required string Text { get; init; }
    public required string Category { get; init; }
    public required int MatchCount { get; init; }
}
