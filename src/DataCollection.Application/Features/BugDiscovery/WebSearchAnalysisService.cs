using DataCollection.Application.Common.Services;
using DataCollection.Application.Features.PatternMatching;
using DataCollection.Core.Models;
using DataCollection.Infrastructure.Clients.WebSearch;
using DataCollection.Infrastructure.Models.BugList;
using DataCollection.Infrastructure.Models.WebSearch;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace DataCollection.Application.Features.BugDiscovery;

public class WebSearchAnalysisService(
    IWebSearchService webSearchService,
    IPatternMatchingService patternMatchingService,
    ILogger<WebSearchAnalysisService> logger,
    ChatClient chatClient,
    LlmPromptService llmPromptService,
    ValidationService validationService
)
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a",
        "an",
        "the",
        "and",
        "or",
        "but",
        "in",
        "on",
        "at",
        "to",
        "for",
        "of",
        "with",
        "by",
        "using",
        "based",
        "approach",
        "method",
        "technique",
        "algorithm",
        "system",
        "framework",
    };

    public async Task<WebAnalysisResult> SearchForArtifactsIfNeeded(
        Paper paper,
        BugListDiscoveryResult existingResult,
        CancellationToken cancellationToken
    )
    {
        validationService.ValidatePaper(paper);
        ArgumentNullException.ThrowIfNull(existingResult);

        if (!ShouldPerformWebSearch(existingResult))
        {
            LogSkippingWebSearch(existingResult);
            return new WebAnalysisResult();
        }

        LogWebSearchAnalysis(paper.Doi, "started");
        return await PerformWebSearch(paper, cancellationToken);
    }

    private void LogWebSearchAnalysis(string paperDoi, string action, int? queryCount = null)
    {
        if (queryCount.HasValue)
            logger.LogInformation(
                "Web search {Action} for paper {PaperDoi} with {QueryCount} queries",
                action,
                paperDoi,
                queryCount.Value
            );
        else
            logger.LogInformation("Web search {Action} for paper {PaperDoi}", action, paperDoi);
    }

    public static bool ShouldPerformWebSearch(BugListDiscoveryResult result) =>
        !HasHighConfidenceArtifacts(result);

    private static bool HasHighConfidenceArtifacts(BugListDiscoveryResult result) =>
        result.ArtifactRepositories.Any(r =>
            r.Confidence >= BugDiscoveryConstants.HighConfidenceThreshold
        );

    private void LogSkippingWebSearch(BugListDiscoveryResult result)
    {
        var highConfidenceRepos = result
            .ArtifactRepositories.Where(r =>
                r.Confidence >= BugDiscoveryConstants.HighConfidenceThreshold
            )
            .ToList();

        if (highConfidenceRepos.Count > 0)
            logger.LogInformation(
                "Skipping web search - found {Count} high-confidence repositories: {Repos}",
                highConfidenceRepos.Count,
                string.Join(", ", highConfidenceRepos.Select(r => r.Url).Take(3))
            );
        else
            logger.LogDebug("Skipping web search - no high-confidence artifacts needed");
    }

    private async Task<WebAnalysisResult> PerformWebSearch(
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        var queries = GenerateSearchQueries(paper);
        LogWebSearchAnalysis(paper.Doi, "generated queries", queries.Count);

        var searchTasks = queries
            .Take(BugDiscoveryConstants.MaxSearchAttempts)
            .AsParallel()
            .TakeWhile(_ => !cancellationToken.IsCancellationRequested)
            .Select(q => webSearchService.SearchAsync(q, maxResults: 10, cancellationToken));

        var searchResults = (await Task.WhenAll(searchTasks)).SelectMany(r => r);

        var bugLists = new List<BugListSource>();
        var artifactRepositories = new List<ArtifactRepository>();

        await ProcessSearchResults(
            searchResults,
            bugLists,
            artifactRepositories,
            paper,
            cancellationToken
        );

        return new WebAnalysisResult
        {
            BugLists = bugLists.DistinctBy(b => b.Url).ToList(),
            ArtifactRepositories = artifactRepositories.DistinctBy(r => r.Url).ToList(),
        };
    }

    private async Task ProcessSearchResults(
        IEnumerable<WebSearchResult> searchResults,
        List<BugListSource> bugLists,
        List<ArtifactRepository> artifactRepositories,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        var uniqueUrls = searchResults.Select(r => r.Url).Distinct().ToList();
        logger.LogDebug("Processing {Count} unique search results", uniqueUrls.Count);

        foreach (var url in uniqueUrls.TakeWhile(_ => !cancellationToken.IsCancellationRequested))
            await ProcessSingleSearchResult(
                url,
                bugLists,
                artifactRepositories,
                paper,
                cancellationToken
            );
    }

    private List<string> GenerateSearchQueries(Paper paper)
    {
        validationService.ValidatePaper(paper);

        var queries = new List<string>();

        AddPaperBasedQueries(queries, paper);

        return queries.Distinct().Take(BugDiscoveryConstants.MaxSearchAttempts).ToList();
    }

    private void AddPaperBasedQueries(List<string> queries, Paper paper)
    {
        var firstAuthor = ExtractFirstAuthorLastName(paper);

        if (!string.IsNullOrEmpty(firstAuthor))
        {
            queries.Add($"{firstAuthor} repository");
            queries.Add($"{firstAuthor} source code");
            queries.Add($"{firstAuthor} github");
            queries.Add($"{firstAuthor} implementation");
        }

        var titleWords = ExtractSignificantTitleWords(paper.Title);
        if (titleWords.Count <= 0)
            return;
        var titlePhrase = string.Join(" ", titleWords);
        queries.Add($"{titlePhrase} repository");
        queries.Add($"{titlePhrase} implementation");
    }

    private string ExtractFirstAuthorLastName(Paper paper) =>
        paper.Authors.FirstOrDefault()?.Split(' ').LastOrDefault() ?? "";

    private static List<string> ExtractSignificantTitleWords(string title) =>
        string.IsNullOrEmpty(title)
            ? []
            : title
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(word => word.Length > 3 && !StopWords.Contains(word))
                //.Take(5)
                .ToList();

    private async Task ProcessSingleSearchResult(
        string url,
        List<BugListSource> bugLists,
        List<ArtifactRepository> artifactRepositories,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        var repoMatches = await patternMatchingService.FindRepositoryUrlsAsync(
            url,
            cancellationToken
        );
        var bugMatches = await patternMatchingService.FindBugTrackingUrlsAsync(
            url,
            cancellationToken
        );

        if (repoMatches.Count != 0)
            await ProcessRepositorySearchResult(
                url,
                artifactRepositories,
                paper,
                cancellationToken
            );
        else if (bugMatches.Any())
            await ProcessBugTrackingSearchResult(url, bugLists, cancellationToken);
    }

    private async Task ProcessRepositorySearchResult(
        string url,
        List<ArtifactRepository> artifactRepositories,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        if (artifactRepositories.Any(r => r.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
            return; // Already exists

        if (await VerifyRepositoryWithLlm(url, paper, cancellationToken))
            await AddWebSearchRepository(url, artifactRepositories, paper, cancellationToken);
        else
            LogRejectedWebSearchRepository(url, paper);
    }

    private async Task<bool> VerifyRepositoryWithLlm(
        string url,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        var prompt = llmPromptService.CreateRepositoryRelevancePrompt(paper, url);

        var messages = new[] { new UserChatMessage(prompt) };
        var response = await chatClient.CompleteChatAsync(
            messages,
            cancellationToken: cancellationToken
        );
        return response?.Value?.Content?.FirstOrDefault()?.Text?.Trim().ToUpperInvariant() == "YES";
    }

    private async Task AddWebSearchRepository(
        string url,
        List<ArtifactRepository> artifactRepositories,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        var urlType = await patternMatchingService.DetermineUrlTypeAsync(url, cancellationToken);
        artifactRepositories.Add(
            new ArtifactRepository
            {
                Url = url,
                Type = urlType,
                DiscoveryMethod = "Web Search + LLM Verification",
            }
        );

        logger.LogInformation(
            "Added web search repository {Url} for paper {PaperDoi}",
            url,
            paper.Doi
        );
    }

    private void LogRejectedWebSearchRepository(string url, Paper paper) =>
        logger.LogDebug(
            "Rejected web search repository {Url} for paper {PaperDoi} - LLM verification failed",
            url,
            paper.Doi
        );

    private async Task ProcessBugTrackingSearchResult(
        string url,
        List<BugListSource> bugLists,
        CancellationToken cancellationToken
    )
    {
        if (bugLists.Any(b => b.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
            return;

        var urlType = await patternMatchingService.DetermineUrlTypeAsync(url, cancellationToken);
        var confidence = patternMatchingService.CalculateConfidence(url, "WebSearch", 1);

        bugLists.Add(
            new BugListSource
            {
                Url = url,
                Type = urlType,
                DiscoveryMethod = "Web Search",
                Confidence = confidence,
                TableContext = "Found via web search",
            }
        );

        logger.LogDebug("Added bug tracking URL from web search: {Url}", url);
    }
}
