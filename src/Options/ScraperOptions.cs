using System.Collections.Generic;

namespace DataCollection.Options;

public class ScraperOptions
{
    public string AcmBaseUrl { get; set; } = string.Empty;
    public Dictionary<string, string> Cookies { get; set; } = new();
}
