using System;

namespace PdfPlumber
{
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
        public PDFStructElement[] Children { get; set; }
    }

    public class PDFStructElement : Findable
    {
        public string Type { get; set; }
        public PDFStructElement[] Children { get; set; }
    }
}
