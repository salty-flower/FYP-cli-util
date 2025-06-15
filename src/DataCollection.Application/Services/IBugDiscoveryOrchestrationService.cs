using CSharpFunctionalExtensions;
using DataCollection.Application.Models.Commands;
using DataCollection.Core.Models.Errors;
using DataCollection.Infrastructure.Models.BugList;

namespace DataCollection.Application.Services;

public interface IBugDiscoveryOrchestrationService
{
    Task<Result<BugListDiscoveryResult, BugDiscoveryError>> DiscoverBugListsAsync(
        DiscoverBugListsCommand command,
        CancellationToken cancellationToken = default
    );

    Task<Result<BugListDiscoveryAnalysis, BugDiscoveryError>> AnalyzeBugListsAsync(
        AnalyzeBugListsCommand command,
        CancellationToken cancellationToken = default
    );

    Task<Result<BugListDiscoverySummary, BugDiscoveryError>> GetDiscoverySummaryAsync(
        List<BugListDiscoveryResult> results,
        CancellationToken cancellationToken = default
    );
}
