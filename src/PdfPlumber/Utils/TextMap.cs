using System;

namespace DataCollection.PdfPlumber.Utils;

public class TextMap
{
    public required string[] Text { get; set; }

    public object extract()
    {
        throw new NotImplementedException();
    }
}

public class WordMap
{
    public required string[] Words { get; set; }

    public object extract()
    {
        throw new NotImplementedException();
    }
}

public class WordExtractor
{
    public WordMap extract()
    {
        throw new NotImplementedException();
    }
}
