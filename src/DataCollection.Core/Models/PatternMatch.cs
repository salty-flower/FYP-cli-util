namespace DataCollection.Core.Models;

public class PatternMatch
{
    public string Value { get; set; } = "";
    public string Pattern { get; set; } = "";
    public string Category { get; set; } = "";
    public double Confidence { get; set; }
    public int Position { get; set; }
    public string Context { get; set; } = "";
}
