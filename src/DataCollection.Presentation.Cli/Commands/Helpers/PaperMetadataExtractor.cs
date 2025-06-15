namespace DataCollection.Presentation.Cli.Commands.Helpers;

public static class PaperMetadataExtractor
{
    public static Dictionary<string, string> ExtractMetadata(string doi, string content)
    {
        var metadata = new Dictionary<string, string>
        {
            ["doi"] = doi,
            ["search_context"] = "academic_paper",
            ["title"] = ExtractTitle(content),
            ["authors"] = ExtractAuthors(content),
            ["year"] = ExtractYear(doi),
        };
        return metadata;
    }

    private static string ExtractTitle(string content)
    {
        var lines = content.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
        var titleCandidates = lines
            .Where(line => line.Length > 20 && line.Length < 200)
            .Where(line => !line.StartsWith("DOI:", System.StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.Contains("http", System.StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();

        return titleCandidates.Any() ? titleCandidates.First() : "Unknown Title";
    }

    private static string ExtractAuthors(string content)
    {
        var lines = content.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
        var authorCandidates = lines
            .Where(line => line.Contains(",") && line.Length < 100)
            .Where(line => line.Count(c => c == ' ') >= 2 && line.Count(c => c == ' ') <= 8)
            .Take(2)
            .ToList();

        return authorCandidates.Any() ? string.Join("; ", authorCandidates) : "Unknown Authors";
    }

    private static string ExtractYear(string doi)
    {
        if (string.IsNullOrEmpty(doi))
            return "Unknown Year";
        var yearMatch = System.Text.RegularExpressions.Regex.Match(doi, @"(\d{4})");
        return yearMatch.Success ? yearMatch.Groups[1].Value : "Unknown Year";
    }
}
