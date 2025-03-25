using System;

namespace DataCollection.PdfPlumber;

public class Findable
{
    public object find(string query)
    {
        throw new NotImplementedException();
    }
}

public class StructTreeMissing(string message) : Exception(message) { }

public class PDFStructTree : Findable
{
    public required PDFStructElement[] Children { get; set; }
}

public class PDFStructElement : Findable
{
    public required string Type { get; set; }
    public required PDFStructElement[] Children { get; set; }
}
