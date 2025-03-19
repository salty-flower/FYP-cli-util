using System.Threading;
using ConsoleAppFramework;
using DataCollection.Commands.Repl;

namespace DataCollection.Commands;

/// <summary>
/// Interactive REPL commands for analyzing papers
/// </summary>
[RegisterCommands("repl")]
public class ReplCommands(
    TextLinesReplCommand textLinesReplCommand,
    PdfReplCommand pdfReplCommand,
    MetadataReplCommand metadataReplCommand
)
{
    /// <summary>
    /// Interactive REPL for testing keyword expressions against paper abstract and title
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public void Metadata(CancellationToken cancellationToken = default) =>
        metadataReplCommand.Run(cancellationToken);

    /// <summary>
    /// Interactive REPL for testing keyword expressions against PDF content
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public void PDF(CancellationToken cancellationToken = default) =>
        pdfReplCommand.Run(cancellationToken);

    /// <summary>
    /// Interactive REPL for inspecting PDF text lines
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public void TextLines(CancellationToken cancellationToken = default) =>
        textLinesReplCommand.Run(cancellationToken);

    /// <summary>
    /// Non-interactive command to search in metadata and export results
    /// </summary>
    /// <param name="pattern">Search pattern (regex supported)</param>
    /// <param name="exportPath">Optional path to export results (JSON)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of results found</returns>
    [Command("search-metadata")]
    public int SearchMetadata(
        string pattern,
        string exportPath = null,
        CancellationToken cancellationToken = default
    ) => metadataReplCommand.RunNonInteractiveSearch(pattern, exportPath, cancellationToken);

    /// <summary>
    /// Non-interactive command to search in PDF content and export results
    /// </summary>
    /// <param name="pattern">Search pattern (regex supported)</param>
    /// <param name="exportPath">Optional path to export results (JSON)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of results found</returns>
    [Command("search-pdf")]
    public int SearchPdf(
        string pattern,
        string exportPath = null,
        CancellationToken cancellationToken = default
    ) => pdfReplCommand.RunNonInteractiveSearch(pattern, exportPath, cancellationToken);

    /// <summary>
    /// Non-interactive command to search in PDF text lines and export results
    /// </summary>
    /// <param name="pattern">Search pattern (regex supported)</param>
    /// <param name="exportPath">Optional path to export results (JSON)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of results found</returns>
    [Command("search-textlines")]
    public int SearchTextLines(
        string pattern,
        string exportPath = null,
        CancellationToken cancellationToken = default
    ) => textLinesReplCommand.RunNonInteractiveSearch(pattern, exportPath, cancellationToken);

    /// <summary>
    /// Non-interactive command to evaluate a keyword expression against metadata
    /// </summary>
    /// <param name="expression">Keyword expression to evaluate</param>
    /// <param name="exportPath">Optional path to export results (JSON)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of papers matching the expression</returns>
    [Command("eval-metadata")]
    public int EvaluateMetadata(
        string expression,
        string exportPath = null,
        CancellationToken cancellationToken = default
    ) => metadataReplCommand.RunNonInteractiveEvaluation(expression, exportPath, cancellationToken);

    /// <summary>
    /// Non-interactive command to evaluate a keyword expression against PDF content
    /// </summary>
    /// <param name="expression">Keyword expression to evaluate</param>
    /// <param name="exportPath">Optional path to export results (JSON)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of PDFs matching the expression</returns>
    [Command("eval-pdf")]
    public int EvaluatePdf(
        string expression,
        string exportPath = null,
        CancellationToken cancellationToken = default
    ) => pdfReplCommand.RunNonInteractiveEvaluation(expression, exportPath, cancellationToken);
}
