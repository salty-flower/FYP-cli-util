using CSharpFunctionalExtensions;
using DataCollection.Application.Features.PatternMatching;
using DataCollection.Application.Models.Commands.PatternMatching;
using DataCollection.Core.Models.Errors;

namespace DataCollection.Application.Services;

public interface IPatternMatchingService
{
    Task<Result<List<PatternMatch>, PatternMatchingError>> FindBugTrackingUrlsAsync(
        FindMatchesCommand command,
        CancellationToken cancellationToken = default
    );
    Task<Result<List<PatternMatch>, PatternMatchingError>> FindRepositoryUrlsAsync(
        FindMatchesCommand command,
        CancellationToken cancellationToken = default
    );
    Task<Result<List<PatternMatch>, PatternMatchingError>> FindKeywordMatchesAsync(
        FindMatchesCommand command,
        CancellationToken cancellationToken = default
    );
    Task<Result<string, PatternMatchingError>> DetermineUrlTypeAsync(
        DetermineUrlTypeCommand command,
        CancellationToken cancellationToken = default
    );
    Task<Result<double, PatternMatchingError>> CalculateConfidenceAsync(
        CalculateConfidenceCommand command,
        CancellationToken cancellationToken = default
    );
}
