using CSharpFunctionalExtensions;
using DataCollection.Application.Models.Commands;
using DataCollection.Application.Models.Export.PaperAnalysis;
using DataCollection.Core.Models.Errors;

namespace DataCollection.Application.Services;

public interface IPdfAnalysisService
{
    Task<Result<List<string>, BugDiscoveryError>> ReconstructParagraphsAsync(
        ReconstructParagraphsCommand command,
        CancellationToken cancellationToken = default
    );

    Task<Result<Dictionary<string, int>, BugDiscoveryError>> CountKeywordsAsync(
        CountKeywordsCommand command,
        CancellationToken cancellationToken = default
    );

    Task<Result<Dictionary<string, int>, BugDiscoveryError>> CountKeywordsInTextsAsync(
        CountKeywordsInTextsCommand command,
        CancellationToken cancellationToken = default
    );

    Task<Result<int, BugDiscoveryError>> ExtractBugSentencesAsync(
        ExtractBugSentencesCommand command,
        CancellationToken cancellationToken = default
    );
}
