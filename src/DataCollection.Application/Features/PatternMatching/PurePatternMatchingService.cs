using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;
using DataCollection.Application.Models.Commands.PatternMatching;
using DataCollection.Application.Services;
using DataCollection.Core.Interfaces;
using DataCollection.Core.Models;
using DataCollection.Core.Models.Database;
using DataCollection.Core.Models.Errors;
using DataCollection.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Application.Features.PatternMatching;

public class PurePatternMatchingService : IPatternMatchingService
{
    private readonly IRuleProvider _ruleProvider;
    private readonly ILogger<PurePatternMatchingService> _logger;
    private readonly PatternMatchingOptions _options;
    private readonly string[] _urlCleaningSuffixes;

    public PurePatternMatchingService(
        IRuleProvider ruleProvider,
        ILogger<PurePatternMatchingService> logger,
        IOptions<PatternMatchingOptions> options
    )
    {
        _ruleProvider = ruleProvider;
        _logger = logger;
        _options = options.Value;
        _urlCleaningSuffixes = _options.UrlCleaningSuffixes.ToArray();
    }

    public async Task<Result<List<PatternMatch>, PatternMatchingError>> FindBugTrackingUrlsAsync(
        FindMatchesCommand command,
        CancellationToken cancellationToken = default
    )
    {
        return await FindMatchesAsync(
            command.Text,
            "BugTracking",
            command.ContextWindow,
            cancellationToken
        );
    }

    public async Task<Result<List<PatternMatch>, PatternMatchingError>> FindRepositoryUrlsAsync(
        FindMatchesCommand command,
        CancellationToken cancellationToken = default
    )
    {
        return await FindMatchesAsync(
            command.Text,
            "Repository",
            command.ContextWindow,
            cancellationToken
        );
    }

    public async Task<Result<List<PatternMatch>, PatternMatchingError>> FindKeywordMatchesAsync(
        FindMatchesCommand command,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var keywords = await _ruleProvider.GetKeywordsAsync(
                command.Category,
                cancellationToken
            );
            if (keywords is null || keywords.Count == 0)
            {
                _logger.LogWarning(
                    "No active keywords found for category: {Category}",
                    command.Category
                );
                return Result.Success<List<PatternMatch>, PatternMatchingError>(
                    new List<PatternMatch>()
                );
            }

            var matches = new List<PatternMatch>();
            var normalizedText = command.Text.ToLowerInvariant();

            foreach (var keyword in keywords)
            {
                var index = normalizedText.IndexOf(
                    keyword.Keyword.ToLowerInvariant(),
                    StringComparison.Ordinal
                );
                if (index >= 0)
                {
                    matches.Add(
                        new PatternMatch
                        {
                            Value = keyword.Keyword,
                            Pattern = keyword.Keyword,
                            Category = command.Category,
                            Position = index,
                            Confidence = keyword.BaseConfidence,
                            Context = ExtractContext(
                                command.Text,
                                index,
                                keyword.Keyword.Length,
                                command.ContextWindow
                            ),
                        }
                    );
                }
            }

            return Result.Success<List<PatternMatch>, PatternMatchingError>(matches);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to find keyword matches for category {Category}",
                command.Category
            );
            return Result.Failure<List<PatternMatch>, PatternMatchingError>(
                new PatternMatchingAnalysisError(ex.Message)
            );
        }
    }

    public async Task<Result<string, PatternMatchingError>> DetermineUrlTypeAsync(
        DetermineUrlTypeCommand command,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var rules = await _ruleProvider.GetUrlTypeRulesAsync(cancellationToken);
            foreach (var rule in rules)
            {
                if (Regex.IsMatch(command.Url, rule.Pattern, RegexOptions.IgnoreCase))
                {
                    return Result.Success<string, PatternMatchingError>(rule.Type);
                }
            }
            return Result.Success<string, PatternMatchingError>("Unknown");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to determine URL type for {Url}", command.Url);
            return Result.Failure<string, PatternMatchingError>(
                new PatternMatchingAnalysisError(ex.Message)
            );
        }
    }

    public Task<Result<double, PatternMatchingError>> CalculateConfidenceAsync(
        CalculateConfidenceCommand command,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var baseConfidence = _options.ConfidenceScores.GetValueOrDefault(
                command.Category,
                _options.ConfidenceScores["Default"]
            );

            var boost =
                _options
                    .ConfidenceBoosts.OrderByDescending(b => b.MatchCountThreshold)
                    .FirstOrDefault(b => command.MatchCount > b.MatchCountThreshold)
                    ?.BoostValue ?? 0.0;

            var finalConfidence = Math.Min(1.0, baseConfidence + boost);
            return Task.FromResult(Result.Success<double, PatternMatchingError>(finalConfidence));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to calculate confidence for category {Category}",
                command.Category
            );
            return Task.FromResult(
                Result.Failure<double, PatternMatchingError>(
                    new PatternMatchingAnalysisError(ex.Message)
                )
            );
        }
    }

    private async Task<Result<List<PatternMatch>, PatternMatchingError>> FindMatchesAsync(
        string text,
        string category,
        int contextWindow,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var patterns = await _ruleProvider.GetPatternsAsync(category, cancellationToken);
            if (patterns is null || patterns.Count == 0)
            {
                _logger.LogWarning("No active patterns found for category: {Category}", category);
                return Result.Success<List<PatternMatch>, PatternMatchingError>(
                    new List<PatternMatch>()
                );
            }

            var matches = new List<PatternMatch>();
            // The original service had a "cleanText" which removed spaces. This can be problematic for some regex.
            // I'll stick to the original text for now.
            var textToSearch = text;

            foreach (var pattern in patterns)
            {
                try
                {
                    var regex = new Regex(
                        pattern.Pattern,
                        RegexOptions.IgnoreCase | RegexOptions.Compiled,
                        TimeSpan.FromSeconds(1)
                    );
                    var regexMatches = regex.Matches(textToSearch);

                    foreach (Match match in regexMatches)
                    {
                        matches.Add(
                            new PatternMatch
                            {
                                Value = CleanUrl(match.Value),
                                Pattern = pattern.Pattern,
                                Category = category,
                                Position = match.Index,
                                Confidence = pattern.BaseConfidence,
                                Context = ExtractContext(
                                    text,
                                    match.Index,
                                    match.Length,
                                    contextWindow
                                ),
                            }
                        );
                    }
                }
                catch (RegexMatchTimeoutException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Regex timeout for pattern {Pattern} in category {Category}",
                        pattern.Pattern,
                        category
                    );
                }
            }

            return Result.Success<List<PatternMatch>, PatternMatchingError>(matches);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to find pattern matches for category {Category}",
                category
            );
            return Result.Failure<List<PatternMatch>, PatternMatchingError>(
                new PatternMatchingAnalysisError(ex.Message)
            );
        }
    }

    private string CleanUrl(string url)
    {
        // Trim matching characters from the end of the string.
        return url.TrimEnd(_urlCleaningSuffixes.SelectMany(s => s.ToCharArray()).ToArray());
    }

    private string ExtractContext(string text, int position, int length, int contextWindow)
    {
        var start = Math.Max(0, position - contextWindow);
        var end = Math.Min(text.Length, position + length + contextWindow);
        var actualLength = end - start;
        return text.Substring(start, actualLength);
    }
}
