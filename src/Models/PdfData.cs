﻿using PdfPlumber;

namespace DataCollection.Models;

[MemoryPack.MemoryPackable]
public partial record PdfData
{
    public required string FileName { get; init; }
    public required PlainCell[][][][] Tables { get; init; }
    public required string[] Texts { get; init; }
}

[MemoryPack.MemoryPackable]
public partial record PlainCell
{
    public float X0 { get; init; }
    public float Y0 { get; init; }
    public float X1 { get; init; }
    public float Y1 { get; init; }
    public string Text { get; init; }

    public static explicit operator PlainCell(Cell pyCell) =>
        new PlainCell
        {
            X0 = pyCell.X0,
            Y0 = pyCell.Y0,
            X1 = pyCell.X1,
            Y1 = pyCell.Y1,
            Text = pyCell.Text,
        };
}
