using System;
using System.Collections.Generic;
using DataCollection.Services;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace DataCollection.Commands.Repl;

/// <summary>
/// Base class for REPL commands
/// </summary>
public abstract class BaseReplCommand(ILogger logger, JsonExportService jsonExportService)
{
    /// <summary>
    /// Controls whether to show all results or limit them
    /// </summary>
    protected bool ShowAllResults { get; set; } = false;

    /// <summary>
    /// Last search results for exporting
    /// </summary>
    protected object? LastSearchResults { get; set; }

    /// <summary>
    /// Display a help table for the REPL
    /// </summary>
    protected static void DisplayHelpTable(Dictionary<string, string> commands)
    {
        var table = new Table();
        table.AddColumn("Command");
        table.AddColumn("Description");

        foreach (var command in commands)
        {
            // Escape any potential markup in command keys and descriptions
            string safeKey = ConsoleRenderingService.SafeMarkup(command.Key);
            string safeValue = ConsoleRenderingService.SafeMarkup(command.Value);

            table.AddRow(safeKey, safeValue);
        }

        AnsiConsole.Write(table);
    }

    /// <summary>
    /// Handle errors safely with logging
    /// </summary>
    protected void HandleError(Exception ex, string context)
    {
        logger.LogError(ex, "Error in {Context}: {Message}", context, ex.Message);
        AnsiConsole.MarkupLine($"[red]Error:[/] {ConsoleRenderingService.SafeMarkup(ex.Message)}");
    }

    /// <summary>
    /// Toggle showing all results vs. limited results
    /// </summary>
    protected void HandleShowAllCommand(string[] parts)
    {
        if (parts.Length > 1 && bool.TryParse(parts[1], out bool value))
        {
            ShowAllResults = value;
            AnsiConsole.MarkupLine(
                $"Set showing all results to: [{(ShowAllResults ? "green" : "red")}]{ShowAllResults}[/]"
            );
        }
        else
        {
            // Toggle current value
            ShowAllResults = !ShowAllResults;
            AnsiConsole.MarkupLine(
                $"Toggled showing all results to: [{(ShowAllResults ? "green" : "red")}]{ShowAllResults}[/]"
            );
        }
    }

    /// <summary>
    /// Export the last search results to JSON
    /// This base implementation uses reflection-based serialization.
    /// Derived classes should override this method to use source generation when possible.
    /// </summary>
    protected virtual bool HandleExportCommand(string[] parts, object? data = null)
    {
        if (jsonExportService == null)
        {
            AnsiConsole.MarkupLine("[red]JSON export service is not available[/]");
            return false;
        }

        string filePath = null;
        if (parts.Length > 1)
        {
            filePath = parts[1];
        }

        object? exportData = data ?? LastSearchResults;
        if (exportData == null)
        {
            AnsiConsole.MarkupLine("[red]No data available to export[/]");
            return false;
        }

        try
        {
            // Create default file path if none was provided
            if (string.IsNullOrEmpty(filePath))
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                filePath = System.IO.Path.Combine(
                    System.IO.Directory.GetCurrentDirectory(),
                    $"export-{timestamp}.json"
                );
            }

            logger.LogInformation("Using reflection-based serialization as fallback. Consider implementing source generation for this data type.");

#pragma warning disable CS0618 // Type or member is obsolete
            bool success = jsonExportService.ExportToJson(exportData, filePath);
#pragma warning restore CS0618

            if (success)
            {
                AnsiConsole.MarkupLine(
                    $"[green]Data exported to:[/] {ConsoleRenderingService.SafeMarkup(jsonExportService.GetLastExportedFilePath())}"
                );
                return true;
            }
            else
            {
                AnsiConsole.MarkupLine("[red]Failed to export data[/]");
                return false;
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine(
                $"[red]Error exporting data:[/] {ConsoleRenderingService.SafeMarkup(ex.Message)}"
            );
            return false;
        }
    }
}
