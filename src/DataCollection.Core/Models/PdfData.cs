namespace DataCollection.Core.Models;

public record PdfData
{
    public required string FileName { get; init; }
    public required string[] Texts { get; init; }

    public required MatchObject[][] TextLines { get; init; } // Pages then lines
}

public record BoundingBox(double X0, double Top, double X1, double Bottom);

public record MatchObject(string Text, double X0, double Top, double X1, double Bottom)
    : BoundingBox(X0, Top, X1, Bottom)
{
    public char[]? Chars { get; init; }
}
