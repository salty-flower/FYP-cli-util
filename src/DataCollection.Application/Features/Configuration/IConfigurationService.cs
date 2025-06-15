namespace DataCollection.Application.Features.Configuration;

public interface IConfigurationService
{
    Task<T?> GetValueAsync<T>(string category, string key, T? defaultValue = default);
    Task<List<T>> GetArrayAsync<T>(string category, string key);
    Task<Dictionary<string, T>> GetCategoryAsync<T>(string category);
    Task SetValueAsync<T>(string category, string key, T value, string? description = null);
    Task SetArrayAsync<T>(
        string category,
        string key,
        IEnumerable<T> values,
        string? description = null
    );
    Task<bool> ExistsAsync(string category, string key);
    Task RemoveAsync(string category, string key);
    Task SeedDefaultsAsync();
}

public static class ConfigurationCategories
{
    public const string Constants = "Constants";
    public const string FileNames = "FileNames";
    public const string Keywords = "Keywords";
    public const string StopWords = "StopWords";
    public const string KnownHosts = "KnownHosts";
    public const string ArtifactSections = "ArtifactSections";
    public const string Thresholds = "Thresholds";
    public const string Limits = "Limits";
}

public static class ConfigurationKeys
{
    // Constants
    public const string HighConfidenceThreshold = "HighConfidenceThreshold";
    public const string MediumConfidenceThreshold = "MediumConfidenceThreshold";
    public const string LowConfidenceThreshold = "LowConfidenceThreshold";
    public const string MinimumConfidenceThreshold = "MinimumConfidenceThreshold";
    public const string DefaultMinimumConfidence = "DefaultMinimumConfidence";
    public const string QualityFilterMinConfidence = "QualityFilterMinConfidence";

    // Processing Limits
    public const string MaxRepositoriesPerSearch = "MaxRepositoriesPerSearch";
    public const string MaxIssuesPerRepository = "MaxIssuesPerRepository";
    public const string MaxFilesPerRepository = "MaxFilesPerRepository";
    public const string MaxSearchResults = "MaxSearchResults";

    // File Names
    public const string ReadmeFileNames = "ReadmeFileNames";
    public const string BugRelatedPaths = "BugRelatedPaths";

    // Keywords
    public const string ArtifactKeywords = "ArtifactKeywords";
    public const string BugListKeywords = "BugListKeywords";

    // Known Hosts
    public const string RepositoryHosts = "RepositoryHosts";

    // Stop Words
    public const string SearchStopWords = "SearchStopWords";
    public const string TitleStopWords = "TitleStopWords";

    // Artifact Sections
    public const string ArtifactSectionIdentifiers = "ArtifactSectionIdentifiers";
}
