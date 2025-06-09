using DataCollection.Application.Features.PatternMatching;
using DataCollection.Core.Models;
using DataCollection.Infrastructure.Clients;
using DataCollection.Infrastructure.Models.BugList;
using DataCollection.Infrastructure.Models.WebSearch;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace DataCollection.Application.Features.BugDiscovery;

/// <summary>
/// Service responsible for web search analysis to discover bug lists and artifact repositories
/// </summary>
public class WebSearchAnalysisService
{
    private readonly IWebSearchService webSearchService;
    private readonly GitHubService gitHubService;
    private readonly IPatternMatchingService patternMatchingService;
    private readonly ILogger<WebSearchAnalysisService> logger;
    private readonly ChatClient chatClient;

    // Constants
    private const int MaxSearchAttempts = 5;
    private const double WebSearchConfidenceMultiplier = 0.8;
    private const double LlmVerificationMultiplier = 1.2;
    private const double HighConfidenceThreshold = 0.8;

    // Known repository hosts
    private static readonly string[] KnownRepositoryHosts =
    [
        "github.com",
        "gitlab.com",
        "bitbucket.org",
        "sourceforge.net",
        "code.google.com",
        "launchpad.net",
        "codeplex.com",
    ];

    public WebSearchAnalysisService(
        IWebSearchService webSearchService,
        GitHubService gitHubService,
        IPatternMatchingService patternMatchingService,
        ILogger<WebSearchAnalysisService> logger,
        ChatClient chatClient
    )
    {
        this.webSearchService = webSearchService;
        this.gitHubService = gitHubService;
        this.patternMatchingService = patternMatchingService;
        this.logger = logger;
        this.chatClient = chatClient;
    }

    /// <summary>
    /// Determines if web search is needed and performs it
    /// </summary>
    public async Task<bool> SearchForArtifactsIfNeeded(
        Paper paper,
        BugListDiscoveryResult result,
        CancellationToken cancellationToken
    )
    {
        if (!ShouldPerformWebSearch(result))
        {
            LogSkippingWebSearch(result);
            return false;
        }

        logger.LogInformation("Starting web search for paper {PaperDoi}", paper.Doi);
        return await PerformWebSearch(paper, result, cancellationToken);
    }

    /// <summary>
    /// Checks if web search should be performed based on existing results
    /// </summary>
    public bool ShouldPerformWebSearch(BugListDiscoveryResult result)
    {
        return !HasHighConfidenceArtifacts(result);
    }

    private bool HasHighConfidenceArtifacts(BugListDiscoveryResult result)
    {
        return result.ArtifactRepositories.Any(r => r.Confidence >= HighConfidenceThreshold);
    }

    private void LogSkippingWebSearch(BugListDiscoveryResult result)
    {
        var highConfidenceRepos = result
            .ArtifactRepositories.Where(r => r.Confidence >= HighConfidenceThreshold)
            .ToList();

        if (highConfidenceRepos.Count > 0)
        {
            logger.LogInformation(
                "Skipping web search - found {Count} high-confidence repositories: {Repos}",
                highConfidenceRepos.Count,
                string.Join(", ", highConfidenceRepos.Select(r => r.Url).Take(3))
            );
        }
        else
        {
            logger.LogDebug("Skipping web search - no high-confidence artifacts needed");
        }
    }

    private async Task<bool> PerformWebSearch(
        Paper paper,
        BugListDiscoveryResult result,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var queries = GenerateSearchQueries(paper);
            logger.LogInformation(
                "Generated {QueryCount} search queries for paper {PaperDoi}: {Queries}",
                queries.Count,
                paper.Doi,
                string.Join(", ", queries)
            );

            var searchResults = new List<WebSearchResult>();
            foreach (var query in queries.Take(MaxSearchAttempts))
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var results = await webSearchService.SearchAsync(query, maxResults: 10);
                searchResults.AddRange(results);
            }

            await ProcessSearchResults(searchResults, result, paper, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Web search failed for paper {PaperDoi}", paper.Doi);
            return false;
        }
    }

    private async Task ProcessSearchResults(
        List<WebSearchResult> searchResults,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        var uniqueUrls = searchResults.Select(r => r.Url).Distinct().ToList();
        logger.LogDebug("Processing {Count} unique search results", uniqueUrls.Count);

        foreach (var url in uniqueUrls)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await ProcessSingleSearchResult(url, result, paper, cancellationToken);
        }
    }

    /// <summary>
    /// Searches for a specific project mentioned by LLM
    /// </summary>
    public async Task SearchForProject(
        ArtifactMention mention,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var queries = new List<string>
            {
                $"{mention.ProjectName} repository",
                $"{mention.ProjectName} source code",
                $"{mention.ProjectName} github",
            };

            if (!string.IsNullOrEmpty(mention.Authors))
            {
                queries.Add($"{mention.ProjectName} {mention.Authors}");
            }

            foreach (var query in queries)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var searchResults = await webSearchService.SearchAsync(query, maxResults: 5);

                foreach (var searchResult in searchResults.Take(5)) // Limit results per query
                {
                    if (IsKnownRepositoryHost(searchResult.Url))
                    {
                        await VerifyAndAddProjectRepository(
                            searchResult.Url,
                            mention,
                            result,
                            paper,
                            cancellationToken
                        );
                        break; // Stop after finding first valid repository
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to search for project {ProjectName}",
                mention.ProjectName
            );
        }
    }

    private bool IsKnownRepositoryHost(string url)
    {
        try
        {
            var uri = new Uri(url);
            return KnownRepositoryHosts.Any(host =>
                uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith($".{host}", StringComparison.OrdinalIgnoreCase)
            );
        }
        catch
        {
            return false;
        }
    }

    private async Task VerifyAndAddProjectRepository(
        string url,
        ArtifactMention mention,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (await VerifyRepositoryRelevance(url, paper, mention, cancellationToken))
            {
                await AddProjectRepository(url, mention, result, paper, cancellationToken);
            }
            else
            {
                LogRejectedProjectRepository(url, mention);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to verify project repository {Url}", url);
        }
    }

    private async Task<bool> VerifyRepositoryRelevance(
        string url,
        Paper paper,
        ArtifactMention mention,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var prompt = CreateRepositoryVerificationPrompt(paper, mention, url);
            var messages = new[] { new UserChatMessage(prompt) };
            var response = await chatClient.CompleteChatAsync(
                messages,
                cancellationToken: cancellationToken
            );

            return response?.Value?.Content?.FirstOrDefault()?.Text?.Trim().ToUpperInvariant()
                == "YES";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LLM verification failed for {Url}", url);
            return false;
        }
    }

    private string CreateRepositoryVerificationPrompt(
        Paper paper,
        ArtifactMention mention,
        string url
    )
    {
        return $@"Is this repository likely the implementation for the mentioned project?

Paper: {paper.Title}
Authors: {string.Join(", ", paper.Authors ?? [])}
Project mentioned: {mention.ProjectName}
Project context: {mention.Context}
Repository URL: {url}

Respond with only: YES or NO";
    }

    private async Task AddProjectRepository(
        string url,
        ArtifactMention mention,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        if (
            result.ArtifactRepositories.Any(r =>
                r.Url.Equals(url, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            logger.LogDebug("Repository {Url} already exists in results", url);
            return;
        }

        var confidence = WebSearchConfidenceMultiplier * LlmVerificationMultiplier;
        var context = $"LLM mentioned project: {mention.ProjectName}. Context: {mention.Context}";

        var urlType = await patternMatchingService.DetermineUrlTypeAsync(url, cancellationToken);
        result.ArtifactRepositories.Add(
            new ArtifactRepository
            {
                Url = url,
                Type = urlType,
                DiscoveryMethod = "Web Search + LLM Mention",
                Confidence = Math.Min(1.0, confidence),
            }
        );

        logger.LogInformation(
            "Added project repository {Url} for project {ProjectName} (confidence: {Confidence:F2})",
            url,
            mention.ProjectName,
            confidence
        );
    }

    private void LogRejectedProjectRepository(string url, ArtifactMention mention)
    {
        logger.LogDebug(
            "Rejected project repository {Url} for project {ProjectName} - LLM verification failed",
            url,
            mention.ProjectName
        );
    }

    /// <summary>
    /// Generates search queries based on paper metadata
    /// </summary>
    public List<string> GenerateSearchQueries(Paper paper)
    {
        var queries = new List<string>();

        // Add basic paper-based queries
        AddPaperBasedQueries(queries, paper);

        // Add word-based queries from title
        //AddWordBasedQueries(queries, paper);

        return queries.Distinct().Take(MaxSearchAttempts).ToList();
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
        if (titleWords.Count > 0)
        {
            var titlePhrase = string.Join(" ", titleWords);
            queries.Add($"{titlePhrase} repository");
            queries.Add($"{titlePhrase} implementation");
        }
    }

    private string ExtractFirstAuthorLastName(Paper paper)
    {
        var firstAuthor = paper.Authors?.FirstOrDefault();
        return firstAuthor?.Split(' ').LastOrDefault() ?? "";
    }

    private List<string> ExtractSignificantTitleWords(string title)
    {
        if (string.IsNullOrEmpty(title))
            return [];

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
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

        return title
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 3 && !stopWords.Contains(word))
            //.Take(5)
            .ToList();
    }

    private void AddWordBasedQueries(List<string> queries, Paper paper)
    {
        var significantWords = ExtractSignificantTitleWords(paper.Title);
        var firstAuthor = ExtractFirstAuthorLastName(paper);

        foreach (var word in significantWords.Take(3))
        {
            queries.Add($"{word} repository");

            if (!string.IsNullOrEmpty(firstAuthor))
            {
                queries.Add($"{word} {firstAuthor}");
            }
        }
    }

    private async Task ProcessSingleSearchResult(
        string url,
        BugListDiscoveryResult result,
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

        if (repoMatches.Any())
        {
            await ProcessRepositorySearchResult(url, result, paper, cancellationToken);
        }
        else if (bugMatches.Any())
        {
            await ProcessBugTrackingSearchResult(url, result, cancellationToken);
        }
    }

    private async Task ProcessRepositorySearchResult(
        string url,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        if (
            result.ArtifactRepositories.Any(r =>
                r.Url.Equals(url, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            return; // Already exists
        }

        try
        {
            if (await VerifyRepositoryWithLlm(url, paper, cancellationToken))
            {
                await AddWebSearchRepository(url, result, paper, cancellationToken);
            }
            else
            {
                LogRejectedWebSearchRepository(url, paper);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to process repository search result {Url}", url);
        }
    }

    private async Task<bool> VerifyRepositoryWithLlm(
        string url,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var prompt =
                $@"Is this repository likely an artifact/implementation for this research paper?

Paper: {paper.Title}
Authors: {string.Join(", ", paper.Authors ?? [])}
Repository: {url}

Respond with only: YES or NO";

            var messages = new[] { new UserChatMessage(prompt) };
            var response = await chatClient.CompleteChatAsync(
                messages,
                cancellationToken: cancellationToken
            );
            return response?.Value?.Content?.FirstOrDefault()?.Text?.Trim().ToUpperInvariant()
                == "YES";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LLM verification failed for repository {Url}", url);
            return false;
        }
    }

    private async Task AddWebSearchRepository(
        string url,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        var confidence = WebSearchConfidenceMultiplier * LlmVerificationMultiplier;

        var urlType = await patternMatchingService.DetermineUrlTypeAsync(url, cancellationToken);
        result.ArtifactRepositories.Add(
            new ArtifactRepository
            {
                Url = url,
                Type = urlType,
                DiscoveryMethod = "Web Search + LLM Verification",
                Confidence = Math.Min(1.0, confidence),
            }
        );

        logger.LogInformation(
            "Added web search repository {Url} for paper {PaperDoi} (confidence: {Confidence:F2})",
            url,
            paper.Doi,
            confidence
        );
    }

    private void LogRejectedWebSearchRepository(string url, Paper paper)
    {
        logger.LogDebug(
            "Rejected web search repository {Url} for paper {PaperDoi} - LLM verification failed",
            url,
            paper.Doi
        );
    }

    private async Task ProcessBugTrackingSearchResult(
        string url,
        BugListDiscoveryResult result,
        CancellationToken cancellationToken
    )
    {
        if (result.BugLists.Any(b => b.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var urlType = await patternMatchingService.DetermineUrlTypeAsync(url, cancellationToken);
        var confidence = await patternMatchingService.CalculateConfidenceAsync(
            url,
            "WebSearch",
            1,
            cancellationToken
        );

        result.BugLists.Add(
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

/// <summary>
/// Represents a project mentioned by LLM analysis
/// </summary>
public class ArtifactMention
{
    public string ProjectName { get; set; } = "";
    public string Context { get; set; } = "";
    public string Authors { get; set; } = "";
    public double Confidence { get; set; }
}
