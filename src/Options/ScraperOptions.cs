using System.Collections.Generic;

namespace DataCollection.Options;

public class ScraperOptions
{
    public string AcmBaseUrl { get; set; } = string.Empty;
    public Dictionary<string, string> Cookies { get; set; } = new();
    public ParallelismOptions Parallelism { get; set; } = new();
}

public class ParallelismOptions
{
    public int SectionProcessing { get; set; } = 3;
    public int Downloads { get; set; } = 1;
    public int DownloadDelayMs { get; set; } = 5000;
}
