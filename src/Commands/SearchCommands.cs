using System.Threading;
using ConsoleAppFramework;
using DataCollection.Commands.Repl;
using DataCollection.Options;

namespace DataCollection.Commands;

[RegisterCommands("search")]
[ConsoleAppFilter<PathsOptions.Filter>]
internal class SearchCommands(
    TextLinesReplCommand textLinesReplCommand,
    PdfReplCommand pdfReplCommand,
    MetadataReplCommand metadataReplCommand
)
{
    /// <summary>
    /// Non-interactive command to search in metadata and return results directly
    /// </summary>
    /// <param name="pattern">Search pattern (regex supported)</param>
    /// <param name="exportPath">Optional path to export results (JSON)</param>
    /// <returns>Number of results found</returns>
    public int Metadata(
        string pattern,
        string? exportPath = null,
        CancellationToken cancellationToken = default
    ) => metadataReplCommand.RunNonInteractiveSearch(pattern, out _, exportPath, cancellationToken);

    /// <summary>
    /// Non-interactive command to search in PDF content and export results
    /// </summary>
    /// <param name="pattern">Search pattern (regex supported)</param>
    /// <param name="exportPath">Optional path to export results (JSON)</param>
    /// <returns>Number of results found</returns>
    public int Pdf(
        string pattern,
        string? exportPath = null,
        CancellationToken cancellationToken = default
    ) => pdfReplCommand.RunNonInteractiveSearch(pattern, exportPath, cancellationToken);

    /// <summary>
    /// Non-interactive command to search in PDF text lines and return results directly
    /// </summary>
    /// <param name="pattern">Search pattern (regex supported)</param>
    /// <param name="exportPath">Optional path to export results (JSON)</param>
    /// <returns>Number of results found</returns>
    public int TextLines(
        string pattern,
        string? exportPath = null,
        CancellationToken cancellationToken = default
    ) =>
        textLinesReplCommand.RunNonInteractiveSearch(pattern, out _, exportPath, cancellationToken);
}
