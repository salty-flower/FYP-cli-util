using DataCollection.Application.Features.SemanticAgents.Models;

namespace DataCollection.Application.Models.Export;

public class AgentDiscoveryExport
{
    public required string TaskType { get; set; }
    public DateTime Timestamp { get; set; }
    public int Count { get; set; }
    public required List<DiscoveryResult> Results { get; set; }
}

public class ComprehensiveDiscoveryExport
{
    public required string DOI { get; set; }
    public DateTime Timestamp { get; set; }
    public int TotalResults { get; set; }
    public int DiscoveredArtifacts { get; set; }
    public required List<DiscoveryResult> AllResults { get; set; }
    public required List<DiscoveryResult> Artifacts { get; set; }
}
