namespace DataCollection.Application.Models.Export.BugAnalysis;

/// <summary>
/// Represents the results of bug list discovery for a single paper
/// </summary>
public class BugListDiscoveryResult
{
    /// <summary>
    /// The DOI of the paper
    /// </summary>
    public required string Doi { get; set; }

    /// <summary>
    /// The title of the paper
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// List of discovered bug tracking system URLs (GitHub issues, Jira, Bugzilla, etc.)
    /// </summary>
    public List<BugListSource> BugLists { get; set; } = new();

    /// <summary>
    /// List of discovered artifact repositories
    /// </summary>
    public List<ArtifactRepository> ArtifactRepositories { get; set; } = new();

    /// <summary>
    /// Whether the discovery process was successful
    /// </summary>
    public bool DiscoverySuccessful { get; set; }

    /// <summary>
    /// Error message if discovery failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Number of search attempts made
    /// </summary>
    public int SearchAttempts { get; set; }
}

/// <summary>
/// Represents a bug tracking system source
/// </summary>
public class BugListSource
{
    /// <summary>
    /// URL to the bug tracking system
    /// </summary>
    public required string Url { get; set; }

    /// <summary>
    /// Type of bug tracking system (GitHub, Jira, Bugzilla, etc.)
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// How this bug list was discovered (PDF text, web search, repository link, etc.)
    /// </summary>
    public required string DiscoveryMethod { get; set; }

    /// <summary>
    /// Confidence score (0.0 to 1.0) of this being a valid bug list
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// List of issue numbers extracted from PDF tables (e.g., "#123", "issue-456")
    /// </summary>
    public List<string> IssueNumbers { get; set; } = new();

    /// <summary>
    /// The table or context where this bug list was found
    /// </summary>
    public string? TableContext { get; set; }
}

/// <summary>
/// Represents an artifact repository
/// </summary>
public class ArtifactRepository
{
    /// <summary>
    /// URL to the repository
    /// </summary>
    public required string Url { get; set; }

    /// <summary>
    /// Type of repository (GitHub, GitLab, Bitbucket, etc.)
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// How this repository was discovered
    /// </summary>
    public required string DiscoveryMethod { get; set; }

    /// <summary>
    /// Confidence score (0.0 to 1.0) of this being the correct artifact repository
    /// </summary>
    public double Confidence { get; set; }
}
