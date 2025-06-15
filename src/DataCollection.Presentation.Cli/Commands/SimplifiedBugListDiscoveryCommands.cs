using System.Text.Json;
using ConsoleAppFramework;
using CSharpFunctionalExtensions;
using DataCollection.Application.Models.Commands;
using DataCollection.Application.Services;
using DataCollection.Core.Models.ValueObjects;
using DataCollection.Infrastructure.Models.BugList;
using DataCollection.Infrastructure.Options;
using DataCollection.Presentation.Cli.Filters;
using DataCollection.Presentation.Cli.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace DataCollection.Presentation.Cli.Commands;

[RegisterCommands("buglist-v2")]
[ConsoleAppFilter<PathsOptionsFilter>]
public class SimplifiedBugListDiscoveryCommands(
    IBugListDiscoveryService discoveryService,
    IResultRenderer<BugListDiscoveryAnalysis> resultRenderer,
    IErrorRenderer errorRenderer,
    ILogger<SimplifiedBugListDiscoveryCommands> logger,
    IOptions<PathsOptions> pathsOptions
)
{
    private readonly PathsOptions _pathsOptions = pathsOptions.Value;

    public async Task<int> Discover(
        string dois,
        string? outputPath = null,
        CancellationToken cancellationToken = default
    )
    {
        return await ParseDoisAndExecute(dois, outputPath, cancellationToken);
    }

    public async Task<int> FromFile(
        string doiFile,
        string? outputPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var doiListResult = await LoadDoisFromFile(doiFile, cancellationToken);

        return await doiListResult.Match(
            async dois => await ExecuteDiscovery(dois, outputPath, cancellationToken),
            async error =>
            {
                await errorRenderer.RenderAsync(error, cancellationToken);
                return 1;
            }
        );
    }

    private async Task<int> ParseDoisAndExecute(
        string doiInput,
        string? outputPath,
        CancellationToken cancellationToken
    )
    {
        var parseResult = ParseDois(doiInput);

        return await parseResult.Match(
            async dois => await ExecuteDiscovery(dois, outputPath, cancellationToken),
            async error =>
            {
                await errorRenderer.RenderAsync(error, cancellationToken);
                return 1;
            }
        );
    }

    private static Result<IReadOnlyList<Doi>, object> ParseDois(string doiInput)
    {
        if (string.IsNullOrWhiteSpace(doiInput))
            return "DOIs parameter is required";

        var doiStrings = doiInput
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.Trim())
            .ToList();

        if (doiStrings.Count == 0)
            return "No valid DOIs provided";

        var dois = new List<Doi>();
        foreach (var doiString in doiStrings)
        {
            var doiResult = Doi.Create(doiString);
            if (doiResult.IsFailure)
                return doiResult.Error;

            dois.Add(doiResult.Value);
        }

        return dois.AsReadOnly();
    }

    private async Task<Result<IReadOnlyList<Doi>, object>> LoadDoisFromFile(
        string filePath,
        CancellationToken cancellationToken
    )
    {
        if (!File.Exists(filePath))
            return $"DOI file not found: {filePath}";

        try
        {
            var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
            var cleanedLines = lines
                .Where(line =>
                    !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#')
                )
                .Select(line => line.Trim())
                .ToList();

            if (cleanedLines.Count == 0)
                return "No valid DOIs found in file";

            AnsiConsole.MarkupLine(
                $"[blue]Loaded {cleanedLines.Count} DOIs from file {filePath}[/]"
            );

            var dois = new List<Doi>();
            foreach (var doiString in cleanedLines)
            {
                var doiResult = Doi.Create(doiString);
                if (doiResult.IsFailure)
                    return doiResult.Error;

                dois.Add(doiResult.Value);
            }

            return dois.AsReadOnly();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reading DOI file {FilePath}", filePath);
            return $"Error reading file: {ex.Message}";
        }
    }

    private async Task<int> ExecuteDiscovery(
        IReadOnlyList<Doi> dois,
        string? outputPath,
        CancellationToken cancellationToken
    )
    {
        AnsiConsole.MarkupLine($"[blue]Starting bug list discovery for {dois.Count} DOI(s)...[/]");

        var command = new DiscoverBugListsCommand(dois);
        var result = await discoveryService.DiscoverBugListsAsync(command, cancellationToken);

        return await result.Match(
            async analysis =>
            {
                await resultRenderer.RenderAsync(analysis, cancellationToken);
                await ExportResults(analysis, outputPath);
                await SaveToStorage(analysis, cancellationToken);
                LogSuccessfulCompletion(analysis);
                return 0;
            },
            async error =>
            {
                await errorRenderer.RenderAsync(error, cancellationToken);
                return 1;
            }
        );
    }

    private async Task ExportResults(BugListDiscoveryAnalysis analysis, string? outputPath)
    {
        try
        {
            var finalPath =
                outputPath ?? Path.Combine(_pathsOptions.BaseDir, "bug-list-discovery.json");
            var json = JsonSerializer.Serialize(
                analysis,
                new JsonSerializerOptions { WriteIndented = true }
            );
            await File.WriteAllTextAsync(finalPath, json);
            AnsiConsole.MarkupLine($"[green]Results exported to {finalPath}[/]");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to export results");
            AnsiConsole.MarkupLine($"[red]Failed to export results: {ex.Message}[/]");
        }
    }

    private async Task SaveToStorage(
        BugListDiscoveryAnalysis analysis,
        CancellationToken cancellationToken
    )
    {
        var result = await discoveryService.SaveToStorageAsync(analysis, cancellationToken);

        await result.Match(
            _ => Task.CompletedTask,
            async error =>
            {
                logger.LogWarning("Failed to save to storage");
                await errorRenderer.RenderAsync(error, cancellationToken);
            }
        );
    }

    private void LogSuccessfulCompletion(BugListDiscoveryAnalysis analysis)
    {
        logger.LogInformation(
            "Discovery completed successfully. Papers: {PaperCount}, Bug Lists: {BugListCount}, Artifacts: {ArtifactCount}",
            analysis.Summary.TotalPapersProcessed,
            analysis.Summary.TotalBugListsFound,
            analysis.Summary.TotalArtifactsFound
        );
    }
}
