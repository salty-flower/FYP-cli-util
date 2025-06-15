using CSharpFunctionalExtensions;
using DataCollection.Application.Features.BugDiscovery;
using DataCollection.Application.Models.Commands;
using DataCollection.Core.Models.Errors;
using DataCollection.Infrastructure.Models.BugList;
using DataCollection.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Application.Services;

public class PureBugDiscoveryOrchestrationService : IBugDiscoveryOrchestrationService
{
    private readonly BugListDiscoveryService discoveryService;
    private readonly ILogger<PureBugDiscoveryOrchestrationService> logger;
    private readonly BugDiscoveryOptions options;

    public PureBugDiscoveryOrchestrationService(
        BugListDiscoveryService discoveryService,
        ILogger<PureBugDiscoveryOrchestrationService> logger,
        IOptions<BugDiscoveryOptions> options
    )
    {
        this.discoveryService = discoveryService;
        this.logger = logger;
        this.options = options.Value;
    }

    public async Task<Result<BugListDiscoveryResult, BugDiscoveryError>> DiscoverBugListsAsync(
        DiscoverBugListsCommand command,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            logger.LogInformation(
                "Starting bug list discovery for paper {Doi}: {Title}",
                command.Paper.Doi,
                command.Paper.Title
            );

            if (command.Paper == null || string.IsNullOrWhiteSpace(command.Paper.Doi))
                return new BugDiscoveryConfigurationError("Paper", "Invalid paper or DOI");

            var result = await discoveryService.DiscoverBugListsAsync(
                command.Paper,
                cancellationToken
            );

            var filteredResult = ApplyDiscoveryFilters(result, command);

            logger.LogInformation(
                "Successfully discovered bug lists for paper {Doi}. Found {BugLists} bug lists and {Repositories} repositories",
                command.Paper.Doi,
                filteredResult.BugLists.Count,
                filteredResult.ArtifactRepositories.Count
            );

            return Result.Success<BugListDiscoveryResult, BugDiscoveryError>(filteredResult);
        }
        catch (OperationCanceledException)
        {
            return new BugDiscoveryConfigurationError("Discovery", "Operation was cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error during bug list discovery for paper {Doi}",
                command.Paper?.Doi
            );
            return new BugDiscoveryConfigurationError("Discovery", ex.Message);
        }
    }

    public async Task<Result<BugListDiscoveryAnalysis, BugDiscoveryError>> AnalyzeBugListsAsync(
        AnalyzeBugListsCommand command,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            logger.LogInformation("Starting batch analysis for {Count} DOIs", command.Dois.Count);

            if (command.Dois == null || !command.Dois.Any())
                return new BugDiscoveryConfigurationError("DOIs", "DOI list cannot be empty");

            var analysis = await discoveryService.DiscoverBugListsAsync(
                command.Dois,
                cancellationToken
            );

            logger.LogInformation(
                "Successfully completed batch analysis for {Count} DOIs. Found {TotalResults} total results",
                command.Dois.Count,
                analysis.PaperResults.Count
            );

            return Result.Success<BugListDiscoveryAnalysis, BugDiscoveryError>(analysis);
        }
        catch (OperationCanceledException)
        {
            return new BugDiscoveryConfigurationError("Analysis", "Operation was cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during batch analysis");
            return new BugDiscoveryConfigurationError("Analysis", ex.Message);
        }
    }

    public async Task<Result<BugListDiscoverySummary, BugDiscoveryError>> GetDiscoverySummaryAsync(
        List<BugListDiscoveryResult> results,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            logger.LogInformation(
                "Generating discovery summary for {Count} results",
                results.Count
            );

            var summary = discoveryService.GetDiscoverySummary(results);

            logger.LogInformation("Successfully generated discovery summary");

            return Result.Success<BugListDiscoverySummary, BugDiscoveryError>(summary);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating discovery summary");
            return new BugDiscoveryConfigurationError("Summary", ex.Message);
        }
    }

    private BugListDiscoveryResult ApplyDiscoveryFilters(
        BugListDiscoveryResult result,
        DiscoverBugListsCommand command
    )
    {
        var filteredBugLists = result
            .BugLists.Where(bl => bl.Confidence >= command.ConfidenceThreshold)
            .ToList();

        var filteredRepositories = result
            .ArtifactRepositories.Where(ar => ar.Confidence >= command.ConfidenceThreshold)
            .ToList();

        return new BugListDiscoveryResult
        {
            Doi = result.Doi,
            Title = result.Title,
            BugLists = filteredBugLists,
            ArtifactRepositories = filteredRepositories,
            DiscoverySuccessful = result.DiscoverySuccessful,
            ErrorMessage = result.ErrorMessage,
            SearchAttempts = result.SearchAttempts,
        };
    }
}
