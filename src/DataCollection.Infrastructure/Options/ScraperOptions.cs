namespace DataCollection.Infrastructure.Options;

public class ScraperOptions
{
    public string AcmBaseUrl { get; init; } = string.Empty;
    public Dictionary<string, string> Cookies { get; init; } = new();
}
