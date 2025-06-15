using System.Text.RegularExpressions;
using DataCollection.Core.Models.Database;
using DataCollection.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DataCollection.Application.Features.PatternMatching;

public interface IPatternMatchingService
{
    Task<List<PatternMatch>> FindBugTrackingUrlsAsync(
        string text,
        CancellationToken cancellationToken = default
    );
    Task<List<PatternMatch>> FindRepositoryUrlsAsync(
        string text,
        CancellationToken cancellationToken = default
    );
    Task<List<PatternMatch>> FindKeywordMatchesAsync(
        string text,
        string category,
        CancellationToken cancellationToken = default
    );
    Task<string> DetermineUrlTypeAsync(string url, CancellationToken cancellationToken = default);
    double CalculateConfidence(string text, string category, int matchCount);
}

public class PatternMatch
{
    public string Value { get; set; } = "";
    public string Pattern { get; set; } = "";
    public string Category { get; set; } = "";
    public double Confidence { get; set; }
    public int Position { get; set; }
    public string Context { get; set; } = "";
}

public class PatternMatchingService(DataCollectionDbContext dbContext) : IPatternMatchingService
{
    private readonly Dictionary<string, List<PatternRule>> _cachedPatterns = new();
    private readonly Dictionary<string, List<KeywordRule>> _cachedKeywords = new();
    private readonly Dictionary<string, List<UrlTypeRule>> _cachedUrlTypes = new();

    public async Task<List<PatternMatch>> FindBugTrackingUrlsAsync(
        string text,
        CancellationToken cancellationToken = default
    )
    {
        var patterns = await GetPatternsAsync("BugTracking", cancellationToken);
        return FindMatches(text, patterns, "BugTracking");
    }

    public async Task<List<PatternMatch>> FindRepositoryUrlsAsync(
        string text,
        CancellationToken cancellationToken = default
    )
    {
        var patterns = await GetPatternsAsync("Repository", cancellationToken);
        return FindMatches(text, patterns, "Repository");
    }

    public async Task<List<PatternMatch>> FindKeywordMatchesAsync(
        string text,
        string category,
        CancellationToken cancellationToken = default
    )
    {
        var keywords = await GetKeywordsAsync(category, cancellationToken);
        return FindKeywordMatches(text, keywords, category);
    }

    public async Task<string> DetermineUrlTypeAsync(
        string url,
        CancellationToken cancellationToken = default
    )
    {
        var rules = await GetUrlTypeRulesAsync(cancellationToken);

        foreach (var rule in rules.OrderByDescending(r => r.Priority))
        {
            if (Regex.IsMatch(url, rule.Pattern, RegexOptions.IgnoreCase))
            {
                return rule.Type;
            }
        }

        return "Unknown";
    }

    public double CalculateConfidence(string text, string category, int matchCount)
    {
        var baseConfidence = category switch
        {
            "PdfAnalysis" => 0.9,
            "KeywordAnalysis" => 0.95,
            "WebSearch" => 0.8,
            _ => 0.7,
        };

        var boost = matchCount switch
        {
            > 50 => 0.1,
            > 10 => 0.05,
            _ => 0.0,
        };

        return Math.Min(1.0, baseConfidence + boost);
    }

    private async Task<List<PatternRule>> GetPatternsAsync(
        string category,
        CancellationToken cancellationToken
    )
    {
        if (_cachedPatterns.TryGetValue(category, out var cached))
            return cached;

        var patterns = await dbContext
            .PatternRules.Where(p => p.Category == category && p.IsActive)
            .OrderBy(p => p.Priority)
            .ToListAsync(cancellationToken);

        _cachedPatterns[category] = patterns;
        return patterns;
    }

    private async Task<List<KeywordRule>> GetKeywordsAsync(
        string category,
        CancellationToken cancellationToken
    )
    {
        if (_cachedKeywords.TryGetValue(category, out var cached))
            return cached;

        var keywords = await dbContext
            .KeywordRules.Where(k => k.Category == category && k.IsActive)
            .OrderBy(k => k.Priority)
            .ToListAsync(cancellationToken);

        _cachedKeywords[category] = keywords;
        return keywords;
    }

    private async Task<List<UrlTypeRule>> GetUrlTypeRulesAsync(CancellationToken cancellationToken)
    {
        if (_cachedUrlTypes.TryGetValue("UrlTypes", out var cached))
            return cached;

        var rules = await dbContext
            .UrlTypeRules.Where(u => u.IsActive)
            .OrderByDescending(u => u.Priority)
            .ToListAsync(cancellationToken);

        _cachedUrlTypes["UrlTypes"] = rules;
        return rules;
    }

    private List<PatternMatch> FindMatches(string text, List<PatternRule> patterns, string category)
    {
        var matches = new List<PatternMatch>();
        var cleanText = text.Replace(" ", "");

        foreach (var pattern in patterns)
        {
            var regex = new Regex(pattern.Pattern, RegexOptions.IgnoreCase);
            var regexMatches = regex.Matches(cleanText);

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
                        Context = ExtractContext(text, match.Index, match.Length),
                    }
                );
            }
        }

        return matches;
    }

    private List<PatternMatch> FindKeywordMatches(
        string text,
        List<KeywordRule> keywords,
        string category
    )
    {
        var matches = new List<PatternMatch>();
        var normalizedText = text.ToLowerInvariant();

        foreach (var keyword in keywords)
        {
            var index = normalizedText.IndexOf(keyword.Keyword.ToLowerInvariant());
            if (index >= 0)
            {
                matches.Add(
                    new PatternMatch
                    {
                        Value = keyword.Keyword,
                        Pattern = keyword.Keyword,
                        Category = category,
                        Position = index,
                        Confidence = keyword.BaseConfidence,
                        Context = ExtractContext(text, index, keyword.Keyword.Length),
                    }
                );
            }
        }

        return matches;
    }

    private string CleanUrl(string url) =>
        url.TrimEnd('.', ',', ';', ')', ']', '}', ' ', '\t', '\n', '\r');

    private string ExtractContext(string text, int position, int length)
    {
        const int contextWindow = 100;
        var start = Math.Max(0, position - contextWindow);
        var end = Math.Min(text.Length, position + length + contextWindow);
        return text.Substring(start, end - start);
    }
}
