using DataCollection.Infrastructure.Models.BugList;

namespace DataCollection.Application.Features.BugDiscovery;

public record RepositoryAnalysisResult
{
    public IReadOnlyList<BugListSource> BugLists { get; init; } = [];
    public IReadOnlyList<ArtifactRepository> ArtifactRepositories { get; init; } = [];
}
