using System.Collections.Generic;

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
    public int TotalPapers { get; set; }

    /// <summary>
    /// Number of papers with successfully discovered bug lists
    /// </summary>
    public int PapersWithBugLists { get; set; }

    /// <summary>
    /// Number of papers with successfully discovered artifact repositories
    /// </summary>
    public int PapersWithArtifacts { get; set; }

    /// <summary>
    /// Number of papers with failed discovery
    /// </summary>
    public int FailedDiscoveries { get; set; }

    /// <summary>
    /// Total number of search attempts made
    /// </summary>
    public int TotalSearchAttempts { get; set; }

    /// <summary>
    /// Success rate as a percentage
    /// </summary>
    public double SuccessRate =>
        TotalPapers > 0
            ? (double)(PapersWithBugLists + PapersWithArtifacts) / TotalPapers * 100
            : 0;
}
