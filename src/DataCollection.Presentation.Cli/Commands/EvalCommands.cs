using System.Diagnostics.CodeAnalysis;
using ConsoleAppFramework;
using DataCollection.Presentation.Cli.Commands.Repl;
using DataCollection.Presentation.Cli.Filters;

namespace DataCollection.Presentation.Cli.Commands;

/// <summary>
/// Non-interactive commands to evaluate expressions or searches
/// and export results to JSON
/// </summary>
[RegisterCommands("eval")]
[ConsoleAppFilter<PathsOptionsFilter>]
public class EvalCommands(PdfReplCommand pdfReplCommand, MetadataReplCommand metadataReplCommand)
{
    /// <summary>
    /// Non-interactive command to evaluate a keyword expression against metadata
    /// </summary>
    /// <param name="expression">Keyword expression to evaluate</param>
    /// <param name="exportPath">Optional path to export results (JSON)</param>
    /// <returns>Number of papers matching the expression</returns>
    public async Task<int> Metadata(
        string expression,
        string? exportPath = null,
        CancellationToken cancellationToken = default
    ) =>
        await metadataReplCommand.RunNonInteractiveEvaluation(
            expression,
            exportPath,
            cancellationToken
        );

    /// <summary>
    /// Non-interactive command to evaluate a keyword expression against PDF content
    /// </summary>
    /// <param name="expression">Keyword expression to evaluate</param>
    /// <param name="exportPath">Optional path to export results (JSON)</param>
    /// <returns>Number of PDFs matching the expression</returns>
    [RequiresUnreferencedCode(
        "Calls RunNonInteractiveEvaluation which requires unreferenced code."
    )]
    [RequiresDynamicCode("Calls RunNonInteractiveEvaluation which requires dynamic code.")]
    public async Task<int> Pdf(
        string expression,
        string? exportPath = null,
        CancellationToken cancellationToken = default
    ) =>
        await pdfReplCommand.RunNonInteractiveEvaluation(expression, exportPath, cancellationToken);
}
