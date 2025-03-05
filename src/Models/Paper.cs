namespace DataCollection.Models;

[MemoryPack.MemoryPackable]
public partial record Paper
{
    public required string Title { get; init; }
    public required string[] Authors { get; init; }
    public required string Abstract { get; init; }
    public required string Url { get; init; }
    public required string Doi { get; init; }

    public string DownloadLink => $"/doi/pdf/{Doi}";
    public string SanitizedDoi => Doi.Replace("/", "-");
}
