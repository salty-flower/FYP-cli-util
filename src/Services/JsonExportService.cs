using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DataCollection.Models;
using Microsoft.Extensions.Logging;

namespace DataCollection.Services;

/// <summary>
/// Service for exporting data to JSON files
/// </summary>
public class JsonExportService
{
    private readonly ILogger<JsonExportService> _logger;
    private readonly JsonSerializerOptions _options;
    private object _lastExportedData;
    private string _lastExportedFile;

    public JsonExportService(ILogger<JsonExportService> logger)
    {
        _logger = logger;
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
    }

    /// <summary>
    /// Export data to a JSON file
    /// </summary>
    public bool ExportToJson<T>(T data, string filePath)
    {
        try
        {
            // Store the data for potential later use
            _lastExportedData = data;
            _lastExportedFile = filePath;

            string json = JsonSerializer.Serialize(data, _options);
            File.WriteAllText(filePath, json);

            _logger.LogInformation("Data exported to {FilePath}", filePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting data to JSON: {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Export search results to JSON
    /// </summary>
    public bool ExportSearchResults(
        List<(int PageNum, int LineNum, MatchObject Line)> results,
        string pattern,
        PdfData pdf,
        string outputPath = null
    )
    {
        var exportData = new
        {
            Pattern = pattern,
            PDF = pdf?.FileName,
            ResultCount = results.Count,
            Timestamp = DateTime.Now,
            Results = results.ConvertAll(r => new
            {
                Page = r.PageNum + 1,
                Line = r.LineNum + 1,
                Text = r.Line.Text,
                BoundingBox = new
                {
                    X0 = r.Line.X0,
                    Top = r.Line.Top,
                    X1 = r.Line.X1,
                    Bottom = r.Line.Bottom,
                },
            }),
        };

        string filePath = outputPath ?? GetDefaultFilePath("search-results");
        return ExportToJson(exportData, filePath);
    }

    /// <summary>
    /// Export metadata search results to JSON
    /// </summary>
    public bool ExportMetadataSearchResults(
        List<(Paper Paper, string Match, string Context)> results,
        string pattern,
        string outputPath = null
    )
    {
        var exportData = new
        {
            Pattern = pattern,
            ResultCount = results.Count,
            Timestamp = DateTime.Now,
            Results = results.ConvertAll(r => new
            {
                Paper = new
                {
                    Title = r.Paper.Title,
                    DOI = r.Paper.Doi,
                    Authors = r.Paper.Authors,
                },
                Match = r.Match,
                Context = r.Context,
            }),
        };

        string filePath = outputPath ?? GetDefaultFilePath("metadata-search-results");
        return ExportToJson(exportData, filePath);
    }

    /// <summary>
    /// Export keyword counts to JSON
    /// </summary>
    public bool ExportKeywordCounts(
        Dictionary<string, int> counts,
        string source,
        string outputPath = null
    )
    {
        var exportData = new
        {
            Source = source,
            Timestamp = DateTime.Now,
            Counts = counts,
        };

        string filePath = outputPath ?? GetDefaultFilePath("keyword-counts");
        return ExportToJson(exportData, filePath);
    }

    /// <summary>
    /// Get the default file path for exports
    /// </summary>
    private string GetDefaultFilePath(string prefix)
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        return Path.Combine(Directory.GetCurrentDirectory(), $"{prefix}-{timestamp}.json");
    }

    /// <summary>
    /// Get the last exported data as a string
    /// </summary>
    public string GetLastExportedDataAsString()
    {
        if (_lastExportedData == null)
            return null;

        try
        {
            return JsonSerializer.Serialize(_lastExportedData, _options);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get the last exported file path
    /// </summary>
    public string GetLastExportedFilePath() => _lastExportedFile;
}
