using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using DataCollection.Commands.Repl;
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
    /// Export data to a JSON file using reflection-based serialization.
    /// Consider using source-generation based methods when possible for better performance.
    /// </summary>
    [Obsolete("Consider using source-generation based methods (ExportToJsonSourceGen) when possible for better performance.")]
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
    /// Export data to a JSON file using source generation
    /// </summary>
    public bool ExportToJsonSourceGen<T>(T data, string filePath, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            // Store the data for potential later use
            _lastExportedData = data;
            _lastExportedFile = filePath;

            string json = JsonSerializer.Serialize(data, typeInfo);
            File.WriteAllText(filePath, json);

            _logger.LogInformation("Data exported to {FilePath} using source generation", filePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting data to JSON using source generation: {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Export search results to JSON using source generation
    /// </summary>
    public bool ExportSearchResults(
        List<(int PageNum, int LineNum, MatchObject Line)> results,
        string pattern,
        PdfData pdf,
        string outputPath = null
    )
    {
        var searchResultItems = new List<SearchResultItem>();
        
        foreach (var r in results)
        {
            var item = new SearchResultItem
            {
                Page = r.PageNum + 1,
                Line = r.LineNum + 1,
                Text = r.Line.Text,
                BoundingBox = new Commands.Repl.BoundingBox
                {
                    X0 = (float)r.Line.X0,
                    Top = (float)r.Line.Top,
                    X1 = (float)r.Line.X1,
                    Bottom = (float)r.Line.Bottom
                }
            };
            searchResultItems.Add(item);
        }
        
        var exportData = new SearchResultsExport
        {
            Pattern = pattern,
            Pdf = pdf?.FileName,
            ResultCount = results.Count,
            Timestamp = DateTime.Now,
            Results = searchResultItems
        };

        string filePath = outputPath ?? GetDefaultFilePath("search-results");
        return ExportToJsonSourceGen(exportData, filePath, ReplJsonContext.Default.SearchResultsExport);
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
        var metadataItems = results.ConvertAll(r => new MetadataSearchItem
        {
            Paper = new PaperReference
            {
                Title = r.Paper.Title,
                DOI = r.Paper.Doi,
                Authors = r.Paper.Authors ?? Array.Empty<string>()
            },
            Match = r.Match,
            Context = r.Context
        });
        
        var exportData = new MetadataSearchResult
        {
            Pattern = pattern,
            TotalMatches = results.Count,
            Timestamp = DateTime.Now,
            Results = metadataItems
        };

        string filePath = outputPath ?? GetDefaultFilePath("metadata-search-results");
        return ExportToJsonSourceGen(exportData, filePath, ReplJsonContext.Default.MetadataSearchResult);
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
        var exportData = new KeywordCountsExport
        {
            Source = source,
            Timestamp = DateTime.Now,
            Counts = counts
        };

        string filePath = outputPath ?? GetDefaultFilePath("keyword-counts");
        return ExportToJsonSourceGen(exportData, filePath, ReplJsonContext.Default.KeywordCountsExport);
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
