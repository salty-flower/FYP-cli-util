using System.ComponentModel.DataAnnotations;

namespace DataCollection.Infrastructure.Models.BugList;

public class BugListDiscoveryAnalysis
{
    public required BugListDiscoverySummary Summary { get; set; }
    public required List<BugListDiscoveryResult> PaperResults { get; set; }
    public Dictionary<string, int> BugTrackingSystemStats { get; set; } = new();
    public Dictionary<string, int> RepositoryTypeStats { get; set; } = new();
}

public class BugListDiscoverySummary
{
    public int TotalPapersProcessed { get; set; }
    public int PapersWithBugLists { get; set; }
    public int PapersWithArtifacts { get; set; }
    public int TotalBugListsFound { get; set; }
    public int TotalArtifactsFound { get; set; }

    [Range(0, 1)]
    public double SuccessRate { get; set; }
    public int TotalPapers => TotalPapersProcessed;
    public int FailedDiscoveries => TotalPapersProcessed - PapersWithSuccessfulDiscovery;
    public int PapersWithSuccessfulDiscovery =>
        TotalPapersProcessed > 0 ? (int)(SuccessRate * TotalPapersProcessed) : 0;
}
