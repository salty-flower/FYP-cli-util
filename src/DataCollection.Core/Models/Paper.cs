namespace DataCollection.Core.Models;

public record Paper
{
    public required string Title { get; init; }
    public required string[] Authors { get; init; }
    public string? Abstract { get; init; }
    public required string Url { get; init; }
    public required string Doi { get; init; }

    public string DownloadLink => $"/doi/pdf/{Doi}";
    public string SanitizedDoi => Doi.Replace("/", "-");
}
