using DataCollection.Infrastructure.Models.BugList;

namespace DataCollection.Application.Features.PaperAnalysis;

public record PdfAnalysisResult
{
    public IReadOnlyList<BugListSource> BugLists { get; init; } = [];
    public IReadOnlyList<ArtifactRepository> ArtifactRepositories { get; init; } = [];
}
