using DataCollection.PdfPlumber;
using MemoryPack;

namespace DataCollection.Models;

[MemoryPackable]
public partial record PdfData
{
    public required string FileName { get; init; }
    public required PlainCell[][][][] Tables { get; init; }
    public required string[] Texts { get; init; }

    public required MatchObject[][] TextLines { get; init; } // Pages then lines
}

[MemoryPackable]
public partial record BoundingBox(double X0, double Top, double X1, double Bottom);

[MemoryPackable]
public partial record MatchObject(string Text, double X0, double Top, double X1, double Bottom)
    : BoundingBox(X0, Top, X1, Bottom)
{
    //public ReadOnlyDictionary<string, object>? Groups { init; get; }

    public char[]? Chars { get; init; }
}

[MemoryPackable]
public partial record PlainCell
{
    public float X0 { get; init; }
    public float Y0 { get; init; }
    public float X1 { get; init; }
    public float Y1 { get; init; }
    public required string Text { get; init; }

    public static explicit operator PlainCell(Cell pyCell) =>
        new()
        {
            X0 = pyCell.X0,
            Y0 = pyCell.Y0,
            X1 = pyCell.X1,
            Y1 = pyCell.Y1,
            Text = pyCell.Text,
        };
}
