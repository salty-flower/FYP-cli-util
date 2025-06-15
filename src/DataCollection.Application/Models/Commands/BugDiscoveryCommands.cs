using DataCollection.Core.Models;

namespace DataCollection.Application.Models.Commands;

public class DiscoverBugListsCommand
{
    public required Paper Paper { get; set; }
    public bool EnablePdfAnalysis { get; set; } = true;
    public bool EnableRepositoryAnalysis { get; set; } = true;
    public bool EnableWebSearch { get; set; } = true;
    public bool EnableLlmVerification { get; set; } = false;
    public double ConfidenceThreshold { get; set; } = 0.8;
    public int MaxSearchAttempts { get; set; } = 5;
    public int ContextWindowSize { get; set; } = 200;
}

public class AnalyzeBugListsCommand
{
    public required List<string> Dois { get; set; }
    public bool EnableParallelProcessing { get; set; } = true;
    public int MaxConcurrency { get; set; } = 5;
    public bool EnableDetailedLogging { get; set; } = false;
    public double ConfidenceThreshold { get; set; } = 0.8;
}

public class ProcessPdfContentCommand
{
    public required Paper Paper { get; set; }
    public bool ExtractDirectUrls { get; set; } = true;
    public bool ExtractStructuredBugLists { get; set; } = true;
    public bool ExtractArtifactsByKeywords { get; set; } = true;
    public bool EnableLlmAnalysis { get; set; } = false;
    public int MaxTextLengthForLlm { get; set; } = 4000;
    public int ContextWindowSize { get; set; } = 200;
}

public class AnalyzeRepositoryCommand
{
    public required string RepositoryUrl { get; set; }
    public required Paper Paper { get; set; }
    public bool EnableContentAnalysis { get; set; } = true;
    public bool EnableStructureAnalysis { get; set; } = true;
    public int RetryDelayMs { get; set; } = 1000;
    public int MaxRetryAttempts { get; set; } = 3;
}

public class PerformWebSearchCommand
{
    public required Paper Paper { get; set; }
    public required string SearchQuery { get; set; }
    public int MaxResults { get; set; } = 10;
    public bool EnableLlmVerification { get; set; } = false;
    public double ConfidenceMultiplier { get; set; } = 0.8;
}

public class BatchProcessIssuesCommand
{
    public required List<(
        string? Url,
        string? Owner,
        string? Repo,
        long? Number
    )> IssueTasks { get; set; }
    public bool SaveResults { get; set; } = true;
    public bool UseCache { get; set; } = true;
    public bool UseOpenAIBatch { get; set; } = true;
    public int MaxParallelTasks { get; set; } = 10;
    public string? BatchJobId { get; set; }
}
