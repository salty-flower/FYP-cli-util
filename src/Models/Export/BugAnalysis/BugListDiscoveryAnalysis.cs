namespace DataCollection.Models.Export.BugAnalysis;

/// <summary>
/// Represents the complete analysis of bug list discovery for multiple papers
/// </summary>
public class BugListDiscoveryAnalysis
{
    /// <summary>
    /// Summary statistics of the discovery process
    /// </summary>
    public required BugListDiscoverySummary Summary { get; set; }

    /// <summary>
    /// Individual results for each paper
    /// </summary>
    public required List<BugListDiscoveryResult> PaperResults { get; set; }

    /// <summary>
    /// Overall statistics about discovered bug tracking systems
    /// </summary>
    public Dictionary<string, int> BugTrackingSystemStats { get; set; } = new();

    /// <summary>
    /// Overall statistics about discovered repository types
    /// </summary>
    public Dictionary<string, int> RepositoryTypeStats { get; set; } = new();
}

/// <summary>
/// Summary statistics for bug list discovery
/// </summary>
public class BugListDiscoverySummary
{
    /// <summary>
    /// Total number of papers processed
    /// </summary>
    public int TotalPapersProcessed { get; set; }

    /// <summary>
    /// Number of papers with successfully discovered bug lists
    /// </summary>
    public int PapersWithBugLists { get; set; }

    /// <summary>
    /// Number of papers with successfully discovered artifact repositories
    /// </summary>
    public int PapersWithArtifacts { get; set; }

    /// <summary>
    /// Total number of bug lists found across all papers
    /// </summary>
    public int TotalBugListsFound { get; set; }

    /// <summary>
    /// Total number of artifact repositories found across all papers
    /// </summary>
    public int TotalArtifactsFound { get; set; }

    /// <summary>
    /// Success rate as a ratio (0.0 to 1.0) representing papers with any findings
    /// This avoids double-counting papers that have both bug lists and artifacts
    /// </summary>
    public double SuccessRate { get; set; }

    /// <summary>
    /// Legacy property for backward compatibility - returns TotalPapersProcessed
    /// </summary>
    public int TotalPapers => TotalPapersProcessed;

    /// <summary>
    /// Number of papers with failed discovery (no findings)
    /// </summary>
    public int FailedDiscoveries => TotalPapersProcessed - PapersWithSuccessfulDiscovery;

    /// <summary>
    /// Number of papers with any successful discovery (bug lists or artifacts)
    /// </summary>
    public int PapersWithSuccessfulDiscovery =>
        TotalPapersProcessed > 0 ? (int)(SuccessRate * TotalPapersProcessed) : 0;
}
