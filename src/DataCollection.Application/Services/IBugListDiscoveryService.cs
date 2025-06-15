using CSharpFunctionalExtensions;
using DataCollection.Application.Models.Commands;
using DataCollection.Core.Models.Errors;
using DataCollection.Infrastructure.Models.BugList;

namespace DataCollection.Application.Services;

public interface IBugListDiscoveryService
{
    Task<Result<BugListDiscoveryAnalysis, BugListDiscoveryError>> DiscoverBugListsAsync(
        DiscoverBugListsCommand command,
        CancellationToken cancellationToken = default
    );

    Task<Result<BugListDiscoveryResult, BugListDiscoveryError>> DiscoverSingleBugListAsync(
        DiscoverSingleBugListCommand command,
        CancellationToken cancellationToken = default
    );

    Task<Result<BugListDiscoveryAnalysis, BugListDiscoveryError>> ComprehensiveDiscoveryAsync(
        ComprehensiveDiscoveryCommand command,
        CancellationToken cancellationToken = default
    );

    Task<Result<bool, BugListDiscoveryError>> SaveToStorageAsync(
        BugListDiscoveryAnalysis analysis,
        CancellationToken cancellationToken = default
    );
}
