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

    // New property for expression-based rules
    public string[] ExpressionRules { get; set; } =
        new[] { "bug > 0", "test >= 3 OR confirm > 0", "(develop > 0 AND detect > 0) OR bug > 5" };
}

public class KeywordBound
{
    public string Keyword { get; set; } = string.Empty;
    public int Min { get; set; } = 0;
    public int Max { get; set; } = 65535;
}
