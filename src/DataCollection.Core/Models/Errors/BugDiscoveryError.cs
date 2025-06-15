using CSharpFunctionalExtensions;

namespace DataCollection.Core.Models.Errors;

public abstract class BugDiscoveryError : ValueObject
{
    public abstract string Code { get; }
    public abstract string Message { get; }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return Message;
    }
}

public sealed class PdfAnalysisError : BugDiscoveryError
{
    public string Doi { get; }
    public string Reason { get; }

    public PdfAnalysisError(string doi, string reason)
    {
        Doi = doi;
        Reason = reason;
    }

    public override string Code => "PDF_ANALYSIS_FAILED";
    public override string Message => $"PDF analysis failed for DOI '{Doi}': {Reason}";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return Doi;
        yield return Reason;
    }
}

public sealed class RepositoryAnalysisError : BugDiscoveryError
{
    public string Repository { get; }
    public string Reason { get; }

    public RepositoryAnalysisError(string repository, string reason)
    {
        Repository = repository;
        Reason = reason;
    }

    public override string Code => "REPOSITORY_ANALYSIS_FAILED";
    public override string Message => $"Repository analysis failed for '{Repository}': {Reason}";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return Repository;
        yield return Reason;
    }
}

public sealed class WebSearchError : BugDiscoveryError
{
    public string Query { get; }
    public string Reason { get; }

    public WebSearchError(string query, string reason)
    {
        Query = query;
        Reason = reason;
    }

    public override string Code => "WEB_SEARCH_FAILED";
    public override string Message => $"Web search failed for query '{Query}': {Reason}";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return Query;
        yield return Reason;
    }
}

public sealed class BugDiscoveryConfigurationError : BugDiscoveryError
{
    public string ConfigurationKey { get; }
    public string Reason { get; }

    public BugDiscoveryConfigurationError(string configurationKey, string reason)
    {
        ConfigurationKey = configurationKey;
        Reason = reason;
    }

    public override string Code => "CONFIGURATION_ERROR";
    public override string Message => $"Configuration error for '{ConfigurationKey}': {Reason}";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return ConfigurationKey;
        yield return Reason;
    }
}

public sealed class BatchProcessingError : BugDiscoveryError
{
    public string BatchId { get; }
    public string Reason { get; }

    public BatchProcessingError(string batchId, string reason)
    {
        BatchId = batchId;
        Reason = reason;
    }

    public override string Code => "BATCH_PROCESSING_FAILED";
    public override string Message => $"Batch processing failed for batch '{BatchId}': {Reason}";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return BatchId;
        yield return Reason;
    }
}
