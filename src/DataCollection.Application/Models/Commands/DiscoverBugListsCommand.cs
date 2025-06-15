using DataCollection.Core.Models.ValueObjects;

namespace DataCollection.Application.Models.Commands;

public sealed record DiscoverBugListsCommand(IReadOnlyList<Doi> Dois);

public sealed record DiscoverSingleBugListCommand(Doi Doi);

public sealed record ComprehensiveDiscoveryCommand(
    Doi Doi,
    bool SkipArtifactAnalysis = false,
    bool SearchExternalPlatforms = true
);

public sealed record SaveToStorageCommand(object Analysis); // Will be typed properly once we resolve the models 
