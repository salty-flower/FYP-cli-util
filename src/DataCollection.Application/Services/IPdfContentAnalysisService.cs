using CSharpFunctionalExtensions;
using DataCollection.Application.Models.Commands;
using DataCollection.Core.Models.Errors;
using DataCollection.Infrastructure.Models.BugList;

namespace DataCollection.Application.Services;

public interface IPdfContentAnalysisService
{
    Task<Result<BugListDiscoveryResult, BugDiscoveryError>> ExtractFromPdfContentAsync(
        ProcessPdfContentCommand command,
        CancellationToken cancellationToken = default
    );

    Task<Result<List<BugListSource>, BugDiscoveryError>> ExtractBugTrackingUrlsAsync(
        ProcessPdfContentCommand command,
        CancellationToken cancellationToken = default
    );

    Task<Result<List<ArtifactRepository>, BugDiscoveryError>> ExtractRepositoryUrlsAsync(
        ProcessPdfContentCommand command,
        CancellationToken cancellationToken = default
    );

    Task<Result<List<BugListSource>, BugDiscoveryError>> ExtractStructuredBugListsAsync(
        ProcessPdfContentCommand command,
        CancellationToken cancellationToken = default
    );
}
