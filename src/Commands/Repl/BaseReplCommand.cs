using System;
using System.Collections.Generic;
using DataCollection.Services;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace DataCollection.Commands.Repl;

/// <summary>
/// Base class for REPL commands
/// </summary>
public abstract class BaseReplCommand(ILogger<BaseReplCommand> logger)
{
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
}
