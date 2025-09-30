using System.Text.Json;
using System.Text.RegularExpressions;
using DataCollection.Application.Models.Export;

namespace DataCollection.Presentation.Cli.Commands.Repl.Helpers;

public static class ReplHelpers
{
    public static List<string> ExtractKeywords(string expression) =>
        [
            .. Regex
                .Matches(expression, @"\b\w+\b")
                .Cast<Match>()
                .Select(m => m.Value.ToLower())
                .Distinct(),
        ];

    public static async Task ExportResults<T>(T data, string filePath)
    {
        var json = JsonSerializer.Serialize(data, typeof(T), ReplJsonContext.Default);
        await File.WriteAllTextAsync(filePath, json);
    }

    public static void DisplaySearchResults<T>(List<T> results, string pattern)
    {
        // Implementation will be added in a future step.
    }
}
