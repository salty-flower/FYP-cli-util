using ConsoleAppFramework;
using DataCollection.Presentation.Cli.Commands.Repl;
using DataCollection.Presentation.Cli.Filters;

namespace DataCollection.Presentation.Cli.Commands;

/// <summary>
/// Interactive REPL commands for analyzing papers
/// </summary>
[RegisterCommands("repl")]
[ConsoleAppFilter<PathsOptionsFilter>]
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
    public async Task Metadata(CancellationToken cancellationToken = default) =>
        await metadataReplCommand.Run(cancellationToken);

    /// <summary>
    /// Interactive REPL for testing keyword expressions against PDF content
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task PDF(CancellationToken cancellationToken = default) =>
        await pdfReplCommand.Run(cancellationToken);

    /// <summary>
    /// Interactive REPL for inspecting PDF text lines
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task TextLines(CancellationToken cancellationToken = default) =>
        await textLinesReplCommand.Run(cancellationToken);
}
