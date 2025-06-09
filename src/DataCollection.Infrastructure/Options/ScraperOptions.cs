namespace DataCollection.Infrastructure.Options;

public class ScraperOptions
{
    public string AcmBaseUrl { get; set; } = string.Empty;
    public Dictionary<string, string> Cookies { get; set; } = new();
}
