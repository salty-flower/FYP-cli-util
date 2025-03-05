using System.Collections.Generic;

namespace DataCollection.Options;

public class KeywordsOptions
{
    public string[] MustExist { get; set; } =
        new[] { "bug", "test", "confirm", "develop", "detect" };
    public string[][] Pairs { get; set; } = new[] { new[] { "confirmed", "acknowledged" } };
    public KeywordBound[] OccurrenceBounds { get; set; } =
        new[]
        {
            new KeywordBound
            {
                Keyword = "bug",
                Min = 1,
                Max = 65535,
            },
        };
    public string[] Analysis { get; set; } =
        new[]
        {
            "bug",
            "develop",
            "acknowledge",
            "maintain",
            "confirm",
            "confirmed",
            "acknowledged",
            "detect",
        };
}

public class KeywordBound
{
    public string Keyword { get; set; } = string.Empty;
    public int Min { get; set; } = 0;
    public int Max { get; set; } = 65535;
}
