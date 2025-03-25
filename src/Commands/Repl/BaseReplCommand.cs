using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DataCollection.Services;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace DataCollection.Commands.Repl;

/// <summary>
/// Base class for REPL commands
/// </summary>
public abstract class BaseReplCommand(ILogger logger)
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
    /// Gets the path of the last exported file
    /// </summary>
    protected string? LastExportedFilePath { get; private set; }

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
    /// Derived classes should override this method.
    /// </summary>
    protected virtual bool HandleExportCommand(string[] parts, object? data = null) =>
        throw new NotImplementedException();

    /// <summary>
    /// Writes serialized JSON data to a file using source generation
    /// </summary>
    /// <typeparam name="T">The type of data being written</typeparam>
    /// <param name="data">The data to write to the file</param>
    /// <param name="filePath">The file path to write to</param>
    /// <param name="typeInfo">Type information for serialization</param>
    /// <returns>True if the operation succeeded, false otherwise</returns>
    protected bool WriteToFile<T>(T data, string filePath, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            // Store the filepath for later reference
            LastExportedFilePath = filePath;

            // Create the directory if it doesn't exist
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Serialize and write the data
            string json = JsonSerializer.Serialize(data, typeInfo);
            File.WriteAllText(filePath, json);

            logger.LogInformation("Data exported to {FilePath} using source generation", filePath);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error exporting data to JSON: {Message}", ex.Message);
            return false;
        }
    }
}
