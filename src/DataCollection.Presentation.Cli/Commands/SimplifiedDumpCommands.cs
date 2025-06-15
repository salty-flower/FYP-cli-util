using ConsoleAppFramework;
using CSharpFunctionalExtensions;
using DataCollection.Application.Models.Commands;
using DataCollection.Application.Services;
using DataCollection.Presentation.Cli.Filters;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace DataCollection.Presentation.Cli.Commands;

[RegisterCommands("dump-v2")]
[ConsoleAppFilter<PathsOptionsFilter>]
[ConsoleAppFilter<PythonEngineInitFilter>]
public class SimplifiedDumpCommands(
    IPdfProcessingService pdfProcessingService,
    ILogger<SimplifiedDumpCommands> logger
)
{
    public async Task<int> Pdf(CancellationToken cancellationToken = default)
    {
        AnsiConsole.MarkupLine("[blue]Starting PDF processing...[/]");

        var command = new ProcessPdfsCommand();
        var result = await pdfProcessingService.ProcessPdfsAsync(command, cancellationToken);

        return await result.Match(
            async processingResult =>
            {
                DisplayResults(processingResult);
                logger.LogInformation(
                    "PDF processing completed successfully. Processed: {ProcessedCount}, Skipped: {SkippedCount}",
                    processingResult.ProcessedFiles,
                    processingResult.SkippedFiles
                );
                return 0;
            },
            async error =>
            {
                AnsiConsole.MarkupLine($"[red]✗[/] PDF processing failed: {error.Message}");
                logger.LogError("PDF processing failed: {Error}", error.Message);
                return 1;
            }
        );
    }

    private static void DisplayResults(PdfProcessingResult result)
    {
        var table = new Table();
        table.AddColumn("Metric");
        table.AddColumn("Count");
        table.Title = new TableTitle("[bold]PDF Processing Results[/]");

        table.AddRow("Total Files", result.TotalFiles.ToString());
        table.AddRow("[green]Processed[/]", result.ProcessedFiles.ToString());
        table.AddRow("[yellow]Skipped[/]", result.SkippedFiles.ToString());

        AnsiConsole.Write(table);

        if (result.ProcessedData.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Sample processed files:[/]");

            foreach (var pdf in result.ProcessedData.Take(5))
            {
                var pageCount = pdf.Texts?.Length ?? 0;
                var lineCount = pdf.TextLines?.Sum(lines => lines?.Length ?? 0) ?? 0;
                AnsiConsole.MarkupLine(
                    $"[dim]  • {pdf.FileName} ({pageCount} pages, {lineCount} text lines)[/]"
                );
            }

            if (result.ProcessedData.Count > 5)
            {
                AnsiConsole.MarkupLine(
                    $"[dim]  ... and {result.ProcessedData.Count - 5} more files[/]"
                );
            }
        }

        AnsiConsole.MarkupLine($"[green]✓[/] PDF processing completed successfully!");
    }
}
