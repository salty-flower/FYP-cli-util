using System.Threading;
using ConsoleAppFramework;
using DataCollection.Commands.Repl;
using DataCollection.Options;

namespace DataCollection.Commands;

/// <summary>
/// Non-interactive commands to evaluate expressions or searches
/// and export results to JSON
/// </summary>
[RegisterCommands("eval")]
[ConsoleAppFilter<PathsOptions.Filter>]
public class EvalCommands(PdfReplCommand pdfReplCommand, MetadataReplCommand metadataReplCommand)
{
    /// <summary>
    /// Non-interactive command to evaluate a keyword expression against metadata
    /// </summary>
    /// <param name="expression">Keyword expression to evaluate</param>
    /// <param name="exportPath">Optional path to export results (JSON)</param>
    /// <returns>Number of papers matching the expression</returns>
    public int Metadata(
        string expression,
        string? exportPath = null,
        CancellationToken cancellationToken = default
    ) => metadataReplCommand.RunNonInteractiveEvaluation(expression, exportPath, cancellationToken);

    /// <summary>
    /// Non-interactive command to evaluate a keyword expression against PDF content
    /// </summary>
    /// <param name="expression">Keyword expression to evaluate</param>
    /// <param name="exportPath">Optional path to export results (JSON)</param>
    /// <returns>Number of PDFs matching the expression</returns>
    public int Pdf(
        string expression,
        string? exportPath = null,
        CancellationToken cancellationToken = default
    ) => pdfReplCommand.RunNonInteractiveEvaluation(expression, exportPath, cancellationToken);
}
