using System.ComponentModel.DataAnnotations;

namespace DataCollection.Infrastructure.Models.BugList;

public class BugListDiscoveryResult
{
    public required string Doi { get; set; }

    public required string Title { get; set; }

    public List<BugListSource> BugLists { get; set; } = [];

    public List<ArtifactRepository> ArtifactRepositories { get; set; } = [];

    public bool DiscoverySuccessful { get; set; }

    public string? ErrorMessage { get; set; }

    public int SearchAttempts { get; set; }
}

public record BugListSource
{
    public required string Url { get; init; }

    public required string Type { get; init; }

    public required string DiscoveryMethod { get; init; }

    [Range(0, 1)]
    public double Confidence { get; init; }

    public IReadOnlyList<string> IssueNumbers { get; init; } = [];

    public string? TableContext { get; init; }
}

public record ArtifactRepository
{
    public required string Url { get; init; }

    public required string Type { get; init; }

    public required string DiscoveryMethod { get; init; }

    [Range(0, 1)]
    public double Confidence { get; set; } // Keep as set for now to fix in-place updates
}
