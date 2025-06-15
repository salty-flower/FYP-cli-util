using CSharpFunctionalExtensions;
using DataCollection.Application.Features.BugDiscovery;
using DataCollection.Application.Models.Commands;
using DataCollection.Core.Models;
using DataCollection.Core.Models.Errors;
using DataCollection.Core.Models.ValueObjects;
using DataCollection.Infrastructure.Clients;
using DataCollection.Infrastructure.Models.BugList;
using DataCollection.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Application.Services;

public class PureBugListDiscoveryService : IBugListDiscoveryService
{
    private readonly BugListDiscoveryService legacyService;
    private readonly DatabaseBugListDiscoveryStorageService storageService;
    private readonly ILogger<PureBugListDiscoveryService> logger;
    private readonly BugListDiscoveryOptions options;

    public PureBugListDiscoveryService(
        BugListDiscoveryService legacyService,
        DatabaseBugListDiscoveryStorageService storageService,
        ILogger<PureBugListDiscoveryService> logger,
        IOptions<BugListDiscoveryOptions> options
    )
    {
        this.legacyService = legacyService;
        this.storageService = storageService;
        this.logger = logger;
        this.options = options.Value;
    }

    public async Task<
        Result<BugListDiscoveryAnalysis, BugListDiscoveryError>
    > DiscoverBugListsAsync(
        DiscoverBugListsCommand command,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var doiStrings = command.Dois.Select(d => d.Value).ToList();
            var analysis = await legacyService.DiscoverBugListsAsync(doiStrings, cancellationToken);
            return Result.Success<BugListDiscoveryAnalysis, BugListDiscoveryError>(analysis);
        }
        catch (OperationCanceledException)
        {
            return new ProcessingFailedError("DiscoverBugLists", "Operation was cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to discover bug lists for {DoiCount} DOIs",
                command.Dois.Count
            );
            return new ProcessingFailedError("DiscoverBugLists", ex.Message);
        }
    }

    public async Task<
        Result<BugListDiscoveryResult, BugListDiscoveryError>
    > DiscoverSingleBugListAsync(
        DiscoverSingleBugListCommand command,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var paper = new Paper { Doi = command.Doi.Value, Title = "Unknown" };
            var result = await legacyService.DiscoverBugListsAsync(paper, cancellationToken);
            return Result.Success<BugListDiscoveryResult, BugListDiscoveryError>(result);
        }
        catch (OperationCanceledException)
        {
            return new ProcessingFailedError("DiscoverSingleBugList", "Operation was cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to discover bug lists for DOI {Doi}", command.Doi);
            return new ProcessingFailedError("DiscoverSingleBugList", ex.Message);
        }
    }

    public async Task<
        Result<BugListDiscoveryAnalysis, BugListDiscoveryError>
    > ComprehensiveDiscoveryAsync(
        ComprehensiveDiscoveryCommand command,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            // For now, use the existing service - this will be fully refactored later
            var doiStrings = new List<string> { command.Doi.Value };
            var analysis = await legacyService.DiscoverBugListsAsync(doiStrings, cancellationToken);
            return Result.Success<BugListDiscoveryAnalysis, BugListDiscoveryError>(analysis);
        }
        catch (OperationCanceledException)
        {
            return new ProcessingFailedError("ComprehensiveDiscovery", "Operation was cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed comprehensive discovery for DOI {Doi}", command.Doi);
            return new ProcessingFailedError("ComprehensiveDiscovery", ex.Message);
        }
    }

    public async Task<Result<bool, BugListDiscoveryError>> SaveToStorageAsync(
        BugListDiscoveryAnalysis analysis,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            await storageService.SaveBugListDiscoveryAnalysis(analysis);
            return Result.Success<bool, BugListDiscoveryError>(true);
        }
        catch (OperationCanceledException)
        {
            return new ProcessingFailedError("SaveToStorage", "Operation was cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save analysis to storage");
            return new StorageFailedError(ex.Message);
        }
    }
}
