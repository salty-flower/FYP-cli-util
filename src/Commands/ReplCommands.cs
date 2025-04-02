using System.Threading;
using ConsoleAppFramework;
using DataCollection.Commands.Repl;
using DataCollection.Options;

namespace DataCollection.Commands;

/// <summary>
/// Interactive REPL commands for analyzing papers
/// </summary>
[RegisterCommands("repl")]
[ConsoleAppFilter<PathsOptions.Filter>]
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
}
