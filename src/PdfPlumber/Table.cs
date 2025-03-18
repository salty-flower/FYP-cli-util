using System;
using System.Collections.Generic;
using System.Linq;
using Python.Runtime;

namespace DataCollection.PdfPlumber;

public class CellGroup
{
    public float[] Cells { get; set; }
}

public class Row : CellGroup { }

public class Column : CellGroup { }

public class UnsetFloat : IConvertible
{
    private readonly dynamic _pyUnsetFloat;

    internal UnsetFloat(dynamic pyUnsetFloat)
    {
        _pyUnsetFloat = pyUnsetFloat;
    }

    public TypeCode GetTypeCode()
    {
        return TypeCode.Single;
    }

    public bool ToBoolean(IFormatProvider provider)
    {
        using (Py.GIL())
        {
            return Convert.ToBoolean(_pyUnsetFloat.As<float>());
        }
    }

    public byte ToByte(IFormatProvider provider)
    {
        using (Py.GIL())
        {
            return Convert.ToByte(_pyUnsetFloat.As<float>());
        }
    }

    public char ToChar(IFormatProvider provider)
    {
        using (Py.GIL())
        {
            return Convert.ToChar(_pyUnsetFloat.As<float>());
        }
    }

    public DateTime ToDateTime(IFormatProvider provider)
    {
        throw new InvalidCastException("Cannot convert UnsetFloat to DateTime");
    }

    public decimal ToDecimal(IFormatProvider provider)
    {
        using (Py.GIL())
        {
            return Convert.ToDecimal(_pyUnsetFloat.As<float>());
        }
    }

    public double ToDouble(IFormatProvider provider)
    {
        using (Py.GIL())
        {
            return _pyUnsetFloat.As<double>();
        }
    }

    public short ToInt16(IFormatProvider provider)
    {
        using (Py.GIL())
        {
            return Convert.ToInt16(_pyUnsetFloat.As<float>());
        }
    }

    public int ToInt32(IFormatProvider provider)
    {
        using (Py.GIL())
        {
            return Convert.ToInt32(_pyUnsetFloat.As<float>());
        }
    }

    public long ToInt64(IFormatProvider provider)
    {
        using (Py.GIL())
        {
            return Convert.ToInt64(_pyUnsetFloat.As<float>());
        }
    }

    public sbyte ToSByte(IFormatProvider provider)
    {
        using (Py.GIL())
        {
            return Convert.ToSByte(_pyUnsetFloat.As<float>());
        }
    }

    public float ToSingle(IFormatProvider provider)
    {
        using (Py.GIL())
        {
            return _pyUnsetFloat.As<float>();
        }
    }

    public string ToString(IFormatProvider provider)
    {
        using (Py.GIL())
        {
            return _pyUnsetFloat.ToString().As<string>();
        }
    }

    public object ToType(Type conversionType, IFormatProvider provider)
    {
        if (conversionType == typeof(float))
            return ToSingle(provider);
        if (conversionType == typeof(double))
            return ToDouble(provider);
        if (conversionType == typeof(int))
            return ToInt32(provider);
        if (conversionType == typeof(string))
            return ToString(provider);

        throw new InvalidCastException($"Cannot convert UnsetFloat to {conversionType.Name}");
    }

    public ushort ToUInt16(IFormatProvider provider)
    {
        using (Py.GIL())
        {
            return Convert.ToUInt16(_pyUnsetFloat.As<float>());
        }
    }

    public uint ToUInt32(IFormatProvider provider)
    {
        using (Py.GIL())
        {
            return Convert.ToUInt32(_pyUnsetFloat.As<float>());
        }
    }

    public ulong ToUInt64(IFormatProvider provider)
    {
        using (Py.GIL())
        {
            return Convert.ToUInt64(_pyUnsetFloat.As<float>());
        }
    }

    public override string ToString()
    {
        return ToString(null);
    }

    public static implicit operator float(UnsetFloat value)
    {
        return value.ToSingle(null);
    }

    public static implicit operator double(UnsetFloat value)
    {
        return value.ToDouble(null);
    }
}

public class Cell
{
    private readonly dynamic _pyCell;

    internal Cell(dynamic pyCell)
    {
        _pyCell = pyCell;
    }

    public float X0 { get; private set; }
    public float Y0 { get; private set; }
    public float X1 { get; private set; }
    public float Y1 { get; private set; }
    public string Text { get; private set; }

    internal void Initialize()
    {
        using (Py.GIL())
        {
            dynamic builtins = Py.Import("builtins");
            bool isDict = builtins.isinstance(_pyCell, builtins.dict).As<bool>();

            if (!isDict)
            {
                // If it's not a dict, treat it as string content
                Text = _pyCell.ToString();
                X0 = 0;
                Y0 = 0;
                X1 = 0;
                Y1 = 0;
            }
            else
            {
                // If it's a dict, extract all properties
                X0 = _pyCell["x0"].As<float>();
                Y0 = _pyCell["y0"].As<float>();
                X1 = _pyCell["x1"].As<float>();
                Y1 = _pyCell["y1"].As<float>();
                Text = _pyCell.get("text", "").As<string>();
            }
        }
    }

    public override string ToString()
    {
        return Text;
    }
}

public class Table
{
    private readonly dynamic _pyTable;
    private Cell[][] _cells;
    private TableRow[] _rows;

    internal Table(dynamic pyTable)
    {
        _pyTable = pyTable;
        Initialize();
    }

    public IReadOnlyList<TableRow> Rows => _rows;
    public IReadOnlyList<IReadOnlyList<Cell>> Cells => _cells;

    private void Initialize()
    {
        using (Py.GIL())
        {
            // _pyTable itself is the list of rows
            int rowCount = _pyTable.__len__().As<int>();
            _cells = new Cell[rowCount][];
            _rows = new TableRow[rowCount];

            for (int i = 0; i < rowCount; i++)
            {
                var pyRow = _pyTable[i];
                int colCount = pyRow.__len__().As<int>();
                _cells[i] = new Cell[colCount];

                for (int j = 0; j < colCount; j++)
                {
                    var cell = new Cell(pyRow[j]);
                    cell.Initialize();
                    _cells[i][j] = cell;
                }

                _rows[i] = new TableRow(_cells[i]);
            }
        }
    }

    public string extractText()
    {
        using (Py.GIL())
        {
            return _pyTable.extract_text().As<string>();
        }
    }

    public override string ToString()
    {
        return extractText();
    }
}

public class TableRow
{
    private readonly Cell[] _cells;

    internal TableRow(Cell[] cells)
    {
        _cells = cells;
    }

    public IReadOnlyList<Cell> Cells => _cells;

    public string extractText()
    {
        return string.Join(" ", _cells.Select(c => c.Text));
    }

    public override string ToString()
    {
        return extractText();
    }
}

public class TableSettings
{
    public float VerticalStrategy { get; set; }
    public float HorizontalStrategy { get; set; }
    public float VerticalTolerance { get; set; }
    public float HorizontalTolerance { get; set; }
}

public class TableFinder
{
    private readonly dynamic _pyPage;

    internal TableFinder(dynamic pyPage)
    {
        _pyPage = pyPage;
    }

    public Table[] findTables(TableSettings settings)
    {
        using (Py.GIL())
        {
            // Convert settings to Python dict
            var pySettings = new PyDict();
            if (settings != null)
            {
                pySettings["vertical_strategy"] = settings.VerticalStrategy.ToPython();
                pySettings["horizontal_strategy"] = settings.HorizontalStrategy.ToPython();
                pySettings["vertical_tolerance"] = settings.VerticalTolerance.ToPython();
                pySettings["horizontal_tolerance"] = settings.HorizontalTolerance.ToPython();
            }

            var pyTables = _pyPage.find_tables(pySettings);
            var tables = new Table[pyTables.__len__().As<int>()];

            for (int i = 0; i < tables.Length; i++)
            {
                tables[i] = new Table(pyTables[i]);
            }

            return tables;
        }
    }
}
