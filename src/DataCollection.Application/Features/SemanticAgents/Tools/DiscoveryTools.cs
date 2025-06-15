using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using DataCollection.Application.Features.SemanticAgents.Models;
using DataCollection.Application.Options;
using DataCollection.Application.Serialization;
using DataCollection.Infrastructure.Clients.IssueTrackers;
using DataCollection.Infrastructure.Clients.WebSearch;
using DataCollection.Infrastructure.Models.WebSearch;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

namespace DataCollection.Application.Features.SemanticAgents.Tools;

public class DiscoveryTools(
    IOptionsSnapshot<KeywordOptions> keywordOptions,
    GitHubClient gitHubClient,
    IWebSearchService webSearchService,
    ILogger<DiscoveryTools> logger
)
{
    [KernelFunction]
    [Description("Search for specific patterns in text content using regex and keyword matching")]
    public string SearchTextPatterns(
        [Description("The text content to search in")] string content,
        [Description(
            "The type of patterns to search for: 'bug_list', 'repository', 'artifact', 'vulnerability'"
        )]
            string patternType,
        [Description("Additional keywords to enhance the search")] string keywords = ""
    )
    {
        try
        {
            var results = new List<DiscoveryResult>();
            var keywordList = keywords
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim().ToLowerInvariant())
                .ToList();

            // Get pattern-specific keywords from configuration
            var keywordsCfg = keywordOptions.Value;
            var configKeywords = patternType.ToLowerInvariant() switch
            {
                "bug_list" => keywordsCfg.BugListKeywords,
                "artifact" => keywordsCfg.ArtifactKeywords,
                _ => new List<string>(),
            };

            var allKeywords = keywordList.Concat(configKeywords).Distinct().ToList();

            // Search for URL patterns
            var urlMatches = ExtractUrlsHelper(content);

            // Search for keyword patterns
            var keywordMatches = SearchKeywords(content, allKeywords);

            // Combine and score results
            foreach (var url in urlMatches)
            {
                var confidence = CalculateConfidence(url, keywordMatches, patternType);
                if (confidence > 0.3) // Minimum threshold
                {
                    results.Add(
                        new DiscoveryResult
                        {
                            Type = patternType,
                            Url = url,
                            Title = ExtractTitleFromContext(content, url),
                            Confidence = confidence,
                            ExtractedKeywords = keywordMatches,
                            Context = ExtractContext(content, url),
                        }
                    );
                }
            }

            return JsonSerializer.Serialize(
                results,
                SemanticAgentsJsonContext.Default.ListDiscoveryResult
            );
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(
                new ErrorResponse { Error = ex.Message },
                SemanticAgentsJsonContext.Default.ErrorResponse
            );
        }
    }

    [KernelFunction]
    [Description("Extract and validate URLs from text content")]
    public Task<string> ExtractUrls(
        [Description("The text content to extract URLs from")] string content
    )
    {
        try
        {
            var urlPattern = @"https?://[^\s<>\)\]\}""']+";
            var matches = Regex.Matches(content, urlPattern, RegexOptions.IgnoreCase);

            var urls = matches
                .Cast<Match>()
                .Select(m => m.Value.TrimEnd('.', ',', ';', ')', ']', '}'))
                .Distinct()
                .Where(IsValidUrl)
                .ToList();

            return Task.FromResult(
                JsonSerializer.Serialize(urls, SemanticAgentsJsonContext.Default.ListString)
            );
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                JsonSerializer.Serialize(
                    new ErrorResponse { Error = ex.Message },
                    SemanticAgentsJsonContext.Default.ErrorResponse
                )
            );
        }
    }

    [KernelFunction]
    [Description("Analyze content context around URLs to determine relevance and extract metadata")]
    public Task<string> AnalyzeUrlContext(
        [Description("The full text content")] string content,
        [Description("The URL to analyze context for")] string url,
        [Description("Number of words to include before and after the URL for context")]
            int contextWords = 20
    )
    {
        try
        {
            var context = ExtractContext(content, url);
            var analysis = new UrlAnalysis
            {
                Url = url,
                Context = context,
                RelevanceScore = CalculateRelevanceScore(context.SurroundingText),
                ExtractedMetadata = ExtractMetadataFromContext(context.SurroundingText),
            };

            return Task.FromResult(
                JsonSerializer.Serialize(analysis, SemanticAgentsJsonContext.Default.UrlAnalysis)
            );
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                JsonSerializer.Serialize(
                    new ErrorResponse { Error = ex.Message },
                    SemanticAgentsJsonContext.Default.ErrorResponse
                )
            );
        }
    }

    [KernelFunction]
    [Description("Validate if URLs are accessible and extract their titles and descriptions")]
    public async Task<string> ValidateUrls(
        [Description("JSON array of URLs to validate")] string urlsJson
    )
    {
        try
        {
            var urls =
                JsonSerializer.Deserialize(urlsJson, SemanticAgentsJsonContext.Default.ListString)
                ?? [];
            var results = new List<UrlValidationResult>();

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            foreach (var url in urls.Take(10)) // Limit to prevent abuse
            {
                try
                {
                    var response = await httpClient.GetAsync(url);
                    var isAccessible = response.IsSuccessStatusCode;
                    var title = "";
                    var description = "";

                    if (
                        isAccessible
                        && response.Content.Headers.ContentType?.MediaType?.StartsWith("text/html")
                            == true
                    )
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        title = ExtractHtmlTitle(content);
                        description = ExtractHtmlDescription(content);
                    }

                    results.Add(
                        new UrlValidationResult
                        {
                            Url = url,
                            Accessible = isAccessible,
                            StatusCode = (int)response.StatusCode,
                            Title = title,
                            Description = description,
                            ContentType = response.Content.Headers.ContentType?.MediaType ?? "",
                        }
                    );
                }
                catch (Exception ex)
                {
                    results.Add(
                        new UrlValidationResult
                        {
                            Url = url,
                            Accessible = false,
                            Error = ex.Message,
                        }
                    );
                }
            }

            return JsonSerializer.Serialize(
                results,
                SemanticAgentsJsonContext.Default.ListUrlValidationResult
            );
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(
                new ErrorResponse { Error = ex.Message },
                SemanticAgentsJsonContext.Default.ErrorResponse
            );
        }
    }

    [KernelFunction]
    [Description("Score and rank discovery results based on confidence, relevance, and validation")]
    public Task<string> ScoreResults(
        [Description("JSON array of discovery results to score")] string resultsJson,
        [Description("Validation results JSON from ValidateUrls")] string validationJson = ""
    )
    {
        try
        {
            var results =
                JsonSerializer.Deserialize(
                    resultsJson,
                    SemanticAgentsJsonContext.Default.ListDiscoveryResult
                ) ?? [];
            var validations = string.IsNullOrEmpty(validationJson)
                ? new Dictionary<string, Dictionary<string, object>>()
                : JsonSerializer
                    .Deserialize(
                        validationJson,
                        SemanticAgentsJsonContext.Default.ListDictionaryStringObject
                    )
                    ?.ToDictionary(v => v["url"].ToString()!, v => v)
                    ?? new Dictionary<string, Dictionary<string, object>>();

            var scoredResults = results
                .Select(result =>
                {
                    var baseScore = result.Confidence;
                    var validationBonus = 0.0;

                    if (validations.TryGetValue(result.Url, out var validation))
                    {
                        if (
                            validation.TryGetValue("accessible", out var accessible)
                            && accessible.ToString() == "True"
                        )
                        {
                            validationBonus += 0.2;
                        }
                        if (
                            validation.TryGetValue("title", out var title)
                            && !string.IsNullOrEmpty(title.ToString())
                        )
                        {
                            validationBonus += 0.1;
                        }
                    }

                    return new
                    {
                        result = result,
                        finalScore = Math.Min(1.0, baseScore + validationBonus),
                        validationInfo = validations.GetValueOrDefault(result.Url),
                    };
                })
                .OrderByDescending(x => x.finalScore)
                .ToList();

            return Task.FromResult(
                JsonSerializer.Serialize(
                    scoredResults,
                    SemanticAgentsJsonContext.Default.ListObject
                )
            );
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                JsonSerializer.Serialize(
                    new ErrorResponse { Error = ex.Message },
                    SemanticAgentsJsonContext.Default.ErrorResponse
                )
            );
        }
    }

    [KernelFunction]
    [Description(
        "Search GitHub repositories by keywords to find bug lists, artifacts, or related repositories. Can perform multiple rounds of search with refined keywords."
    )]
    public async Task<string> SearchGitHubRepositories(
        [Description("Keywords to search for (e.g., 'bug list', 'defects', 'vulnerabilities')")]
            string keywords,
        [Description("Maximum number of repositories to return per search (default: 10)")]
            int maxResults = 10,
        [Description("Language filter (optional, e.g., 'python', 'java')")] string language = "",
        [Description("Additional search qualifiers (e.g., 'stars:>100', 'created:>2020')")]
            string qualifiers = "",
        [Description(
            "Whether to perform multiple search rounds with refined keywords (default: true)"
        )]
            bool multiRoundSearch = true,
        Dictionary<string, string>? taskMetadata = null
    )
    {
        try
        {
            logger.LogInformation(
                "Searching GitHub repositories with keywords: {Keywords}",
                keywords
            );

            var allResults = new List<DiscoveryResult>();
            var searchedQueries = new HashSet<string>();

            // Initial search queries - start with provided keywords
            var searchQueries = new Queue<string>();
            searchQueries.Enqueue(keywords);

            // Add refined search variations if multi-round search is enabled
            if (multiRoundSearch)
            {
                var refinedQueries = GenerateSearchVariations(
                    keywords,
                    language,
                    qualifiers,
                    taskMetadata
                );
                foreach (var query in refinedQueries)
                {
                    searchQueries.Enqueue(query);
                }
            }

            int round = 1;
            var maxRounds = multiRoundSearch ? 3 : 1;

            while (
                searchQueries.Count > 0 && round <= maxRounds && allResults.Count < maxResults * 2
            )
            {
                var currentQuery = searchQueries.Dequeue();

                // Skip if we've already searched this query
                if (searchedQueries.Contains(currentQuery))
                    continue;

                searchedQueries.Add(currentQuery);

                logger.LogDebug("GitHub search round {Round}: {Query}", round, currentQuery);

                try
                {
                    var repositoryNames = await gitHubClient.SearchForRepositoryAsync(currentQuery);

                    foreach (var repoName in repositoryNames.Take(maxResults))
                    {
                        if (string.IsNullOrWhiteSpace(repoName) || !repoName.Contains("/"))
                            continue;

                        var parts = repoName.Split('/');
                        if (parts.Length < 2)
                            continue;

                        var owner = parts[0];
                        var name = parts[1];

                        try
                        {
                            var repoInfo = await gitHubClient.GetRepositoryInfoAsync(owner, name);
                            var repoUrl = $"https://github.com/{repoName}";

                            var result = new DiscoveryResult
                            {
                                Type = "repository",
                                Url = repoUrl,
                                Title = repoInfo.Name ?? name,
                                Description = repoInfo.Description ?? "",
                                Confidence = CalculateGitHubSDKRepoConfidence(
                                    repoInfo,
                                    keywords,
                                    currentQuery
                                ),
                                ExtractedKeywords = ExtractKeywordsFromGitHubRepo(
                                    repoInfo,
                                    keywords
                                ),
                                Context = new DiscoveryContext
                                {
                                    SourceLocation = $"github_search_round_{round}",
                                    SurroundingText = new List<string>
                                    {
                                        repoInfo.Description ?? "",
                                        repoInfo.Name ?? "",
                                    },
                                    RelatedLinks = new List<string> { repoUrl },
                                },
                                Metadata = new Dictionary<string, string>
                                {
                                    ["source"] = "github_sdk_search",
                                    ["search_round"] = round.ToString(),
                                    ["search_query"] = currentQuery,
                                    ["stars"] = repoInfo.StargazersCount?.ToString() ?? "0",
                                    ["language"] = repoInfo.Language ?? "",
                                    ["created_at"] =
                                        repoInfo.CreatedAt?.ToString("yyyy-MM-dd") ?? "",
                                    ["updated_at"] =
                                        repoInfo.UpdatedAt?.ToString("yyyy-MM-dd") ?? "",
                                    ["owner"] = owner,
                                    ["repository"] = name,
                                },
                            };

                            // Only add if not already found and meets minimum confidence
                            if (
                                !allResults.Any(r => r.Url == result.Url)
                                && result.Confidence > 0.3
                            )
                            {
                                allResults.Add(result);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(
                                "Failed to get details for repository {RepoName}: {Error}",
                                repoName,
                                ex.Message
                            );
                        }
                    }

                    // Generate new search queries based on found results if this is early round
                    if (round < maxRounds && multiRoundSearch)
                    {
                        var newQueries = GenerateFollowUpQueries(allResults, keywords);
                        foreach (
                            var newQuery in newQueries.Where(q => !searchedQueries.Contains(q))
                        )
                        {
                            searchQueries.Enqueue(newQuery);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        "GitHub search failed for query '{Query}' in round {Round}: {Error}",
                        currentQuery,
                        round,
                        ex.Message
                    );
                }

                round++;
            }

            // Sort by confidence and take top results
            var finalResults = allResults
                .OrderByDescending(r => r.Confidence)
                .Take(maxResults)
                .ToList();

            logger.LogInformation(
                "Found {Count} GitHub repositories across {Rounds} search rounds",
                finalResults.Count,
                round - 1
            );

            return JsonSerializer.Serialize(
                finalResults,
                SemanticAgentsJsonContext.Default.ListDiscoveryResult
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error searching GitHub repositories: {Message}", ex.Message);
            return JsonSerializer.Serialize(
                new ErrorResponse { Error = ex.Message },
                SemanticAgentsJsonContext.Default.ErrorResponse
            );
        }
    }

    [KernelFunction]
    [Description("Search the web for bug lists, vulnerability databases, or research artifacts")]
    [RequiresUnreferencedCode("Calls SearchWeb which uses dynamic types.")]
    [RequiresDynamicCode("Calls SearchWeb which uses dynamic types.")]
    public async Task<string> SearchWeb(
        [Description("Search query terms")] string query,
        [Description("Maximum number of results to return (default: 10)")] int maxResults = 10,
        [Description("Site restriction (e.g., 'site:github.com', 'site:zenodo.org')")]
            string siteFilter = ""
    )
    {
        try
        {
            logger.LogInformation("Searching web with query: {Query}", query);

            var searchQuery = query;
            if (!string.IsNullOrEmpty(siteFilter))
                searchQuery += $" {siteFilter}";

            var searchResults = await webSearchService.SearchAsync(searchQuery, maxResults);

            var results = searchResults
                .Select(result => new DiscoveryResult
                {
                    Type = "web_result",
                    Url = result.Url,
                    Title = result.Title,
                    Description = result.Snippet,
                    Confidence = CalculateWebResultConfidence(result, query),
                    ExtractedKeywords = ExtractKeywordsFromWebResult(result, query),
                    Context = new DiscoveryContext
                    {
                        SourceLocation = "web_search",
                        SurroundingText = new List<string> { result.Snippet, result.Title },
                        RelatedLinks = new List<string> { result.Url },
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["source"] = "web_search",
                        ["search_engine"] = "duckduckgo",
                    },
                })
                .ToList();

            logger.LogInformation("Found {Count} web search results", results.Count);

            return JsonSerializer.Serialize(
                results,
                SemanticAgentsJsonContext.Default.ListDiscoveryResult
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error searching web: {Message}", ex.Message);
            return JsonSerializer.Serialize(
                new ErrorResponse { Error = ex.Message },
                SemanticAgentsJsonContext.Default.ErrorResponse
            );
        }
    }

    [KernelFunction]
    [Description(
        "Search Zenodo for research datasets and artifacts related to bug lists or vulnerabilities"
    )]
    [RequiresUnreferencedCode("Calls SearchZenodo which uses dynamic types.")]
    [RequiresDynamicCode("Calls SearchZenodo which uses dynamic types.")]
    public async Task<string> SearchZenodo(
        [Description("Search terms for Zenodo API")] string query,
        [Description("Maximum number of results to return (default: 10)")] int maxResults = 10,
        [Description("Resource type filter (e.g., 'dataset', 'software')")] string resourceType = ""
    )
    {
        try
        {
            logger.LogInformation("Searching Zenodo with query: {Query}", query);

            // Construct Zenodo search query
            var zenodoQuery = $"{query} AND (bug OR defect OR vulnerability OR artifact)";
            if (!string.IsNullOrEmpty(resourceType))
                zenodoQuery += $" AND resource_type.type:{resourceType}";

            var webQuery = $"site:zenodo.org {zenodoQuery}";
            var searchResults = await webSearchService.SearchAsync(webQuery, maxResults);

            var results = searchResults
                .Where(r => r.Url.Contains("zenodo.org"))
                .Select(result => new DiscoveryResult
                {
                    Type = "zenodo_artifact",
                    Url = result.Url,
                    Title = result.Title,
                    Description = result.Snippet,
                    Confidence = CalculateZenodoConfidence(result, query),
                    ExtractedKeywords = ExtractKeywordsFromWebResult(result, query),
                    Context = new DiscoveryContext
                    {
                        SourceLocation = "zenodo_search",
                        SurroundingText = new List<string> { result.Snippet, result.Title },
                        RelatedLinks = new List<string> { result.Url },
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["source"] = "zenodo_search",
                        ["platform"] = "zenodo",
                    },
                })
                .ToList();

            logger.LogInformation("Found {Count} Zenodo artifacts", results.Count);

            return JsonSerializer.Serialize(
                results,
                SemanticAgentsJsonContext.Default.ListDiscoveryResult
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error searching Zenodo: {Message}", ex.Message);
            return JsonSerializer.Serialize(
                new ErrorResponse { Error = ex.Message },
                SemanticAgentsJsonContext.Default.ErrorResponse
            );
        }
    }

    // Helper methods
    private static List<string> ExtractUrlsHelper(string content)
    {
        var urlPattern = @"https?://[^\s<>\)\]\}""']+";
        var matches = Regex.Matches(content, urlPattern, RegexOptions.IgnoreCase);

        return matches
            .Cast<Match>()
            .Select(m => m.Value.TrimEnd('.', ',', ';', ')', ']', '}'))
            .Distinct()
            .Where(IsValidUrl)
            .ToList();
    }

    private static List<string> SearchKeywords(string content, List<string> keywords)
    {
        var found = new List<string>();
        var lowerContent = content.ToLowerInvariant();

        foreach (var keyword in keywords)
        {
            if (lowerContent.Contains(keyword.ToLowerInvariant()))
            {
                found.Add(keyword);
            }
        }

        return found;
    }

    private static double CalculateConfidence(string url, List<string> keywords, string patternType)
    {
        var confidence = 0.0;

        // URL pattern matching
        if (patternType == "bug_list" || patternType == "repository")
        {
            if (url.Contains("github.com") || url.Contains("gitlab.com"))
                confidence += 0.3;
            if (url.Contains("issues") || url.Contains("bugs"))
                confidence += 0.4;
            if (url.Contains("jira") || url.Contains("bugzilla"))
                confidence += 0.5;
        }

        // Keyword bonus
        confidence += Math.Min(0.4, keywords.Count * 0.1);

        return Math.Min(1.0, confidence);
    }

    private static bool IsValidUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string ExtractTitleFromContext(string content, string url)
    {
        var urlIndex = content.IndexOf(url, StringComparison.OrdinalIgnoreCase);
        if (urlIndex == -1)
            return "";

        // Look for title in surrounding text
        var start = Math.Max(0, urlIndex - 100);
        var end = Math.Min(content.Length, urlIndex + url.Length + 100);
        var context = content[start..end];

        // Simple title extraction heuristics
        var lines = context.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 10 && trimmed.Length < 200 && !trimmed.Contains("http"))
            {
                return trimmed;
            }
        }

        return "";
    }

    private static DiscoveryContext ExtractContext(string content, string url)
    {
        var urlIndex = content.IndexOf(url, StringComparison.OrdinalIgnoreCase);
        if (urlIndex == -1)
            return new DiscoveryContext();

        var start = Math.Max(0, urlIndex - 200);
        var end = Math.Min(content.Length, urlIndex + url.Length + 200);
        var contextText = content[start..end];

        var surroundingText = contextText
            .Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 2)
            .ToList();

        return new DiscoveryContext
        {
            SurroundingText = surroundingText,
            SourceLocation = $"Character position {urlIndex}",
        };
    }

    private static double CalculateRelevanceScore(List<string> contextWords)
    {
        var relevantWords = new[]
        {
            "bug",
            "issue",
            "defect",
            "repository",
            "artifact",
            "tool",
            "implementation",
        };
        var matches = contextWords.Count(word =>
            relevantWords.Any(relevant => word.ToLowerInvariant().Contains(relevant))
        );

        return Math.Min(1.0, matches / 10.0);
    }

    private static Dictionary<string, string> ExtractMetadataFromContext(List<string> contextWords)
    {
        var metadata = new Dictionary<string, string>();

        // Look for version numbers, dates, etc.
        foreach (var word in contextWords)
        {
            if (Regex.IsMatch(word, @"v?\d+\.\d+"))
            {
                metadata["version"] = word;
            }
            if (Regex.IsMatch(word, @"\d{4}"))
            {
                metadata["year"] = word;
            }
        }

        return metadata;
    }

    private static string ExtractHtmlTitle(string html)
    {
        var titleMatch = Regex.Match(html, @"<title[^>]*>([^<]+)</title>", RegexOptions.IgnoreCase);
        return titleMatch.Success ? titleMatch.Groups[1].Value.Trim() : "";
    }

    private static string ExtractHtmlDescription(string html)
    {
        var descMatch = Regex.Match(
            html,
            @"<meta[^>]*name=[""']description[""'][^>]*content=[""']([^""']+)[""']",
            RegexOptions.IgnoreCase
        );
        return descMatch.Success ? descMatch.Groups[1].Value.Trim() : "";
    }

    private static List<string> GenerateSearchVariations(
        string keywords,
        string language,
        string qualifiers,
        Dictionary<string, string>? taskMetadata = null
    )
    {
        var variations = new List<string>();
        var baseTerms = keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Extract paper-specific metadata for targeted searches
        var paperTitle = taskMetadata?.GetValueOrDefault("paper_title", "");
        var paperDoi = taskMetadata?.GetValueOrDefault("doi", "");
        var paperAuthors = taskMetadata?.GetValueOrDefault("authors", "");

        // If we have paper metadata, prioritize paper-specific searches
        if (!string.IsNullOrEmpty(paperTitle))
        {
            // Search using paper title components
            var titleWords = ExtractSignificantWords(paperTitle);
            foreach (var titleWord in titleWords.Take(3))
            {
                variations.Add($"{titleWord} {string.Join(" ", baseTerms.Take(2))}");
                variations.Add($"{titleWord} implementation");
                variations.Add($"{titleWord} dataset");
                variations.Add($"{titleWord} artifact");
            }

            // Search for the full title with bug/defect terms
            if (titleWords.Count > 2)
            {
                var shortTitle = string.Join(" ", titleWords.Take(3));
                variations.Add($"\"{shortTitle}\" bug");
                variations.Add($"\"{shortTitle}\" defect");
                variations.Add($"\"{shortTitle}\" dataset");
            }
        }

        // Add DOI-based searches if available
        if (!string.IsNullOrEmpty(paperDoi))
        {
            variations.Add($"{paperDoi}");
            variations.Add($"{paperDoi} implementation");
            variations.Add($"{paperDoi} artifact");
        }

        // Add author-based searches if available
        if (!string.IsNullOrEmpty(paperAuthors))
        {
            var authors = paperAuthors
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim().Split(' ').Last()) // Get last names
                .Take(2);
            foreach (var author in authors)
            {
                if (author.Length > 3) // Avoid short names
                {
                    variations.Add($"{author} {string.Join(" ", baseTerms.Take(2))}");
                    variations.Add($"{author} bug dataset");
                }
            }
        }

        // Fall back to generic variations if no paper metadata
        if (variations.Count < 3)
        {
            var bugTerms = new[] { "bug", "defect", "issue", "vulnerability" };
            var listTerms = new[] { "list", "database", "dataset", "collection" };

            foreach (var bugTerm in bugTerms.Take(2))
            {
                foreach (var listTerm in listTerms.Take(2))
                {
                    var query = $"{bugTerm} {listTerm}";
                    if (!string.IsNullOrEmpty(language))
                        query += $" language:{language}";
                    variations.Add(query);
                }
            }

            foreach (var baseTerm in baseTerms.Take(2))
            {
                variations.Add($"dataset {baseTerm}");
                variations.Add($"benchmark {baseTerm}");
            }
        }

        return variations.Take(6).ToList(); // Increased limit for paper-specific searches
    }

    private static List<string> ExtractSignificantWords(string title)
    {
        if (string.IsNullOrEmpty(title))
            return [];

        // Common stop words to exclude
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a",
            "an",
            "and",
            "are",
            "as",
            "at",
            "be",
            "by",
            "for",
            "from",
            "has",
            "he",
            "in",
            "is",
            "it",
            "its",
            "of",
            "on",
            "that",
            "the",
            "to",
            "was",
            "will",
            "with",
            "using",
            "via",
            "through",
        };

        return title
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 3 && !stopWords.Contains(word))
            .Where(word => !word.All(char.IsPunctuation))
            .Take(5)
            .ToList();
    }

    private static List<string> GenerateFollowUpQueries(
        List<DiscoveryResult> existingResults,
        string originalKeywords
    )
    {
        var followUpQueries = new List<string>();

        // Extract common terms from found repositories
        var foundKeywords = existingResults
            .SelectMany(r => r.ExtractedKeywords)
            .GroupBy(k => k)
            .Where(g => g.Count() > 1) // Keywords found in multiple repos
            .Select(g => g.Key)
            .Take(3)
            .ToList();

        // Generate queries combining original terms with discovered terms
        foreach (var foundKeyword in foundKeywords)
        {
            followUpQueries.Add($"{originalKeywords} {foundKeyword}");
            followUpQueries.Add($"{foundKeyword} benchmark");
            followUpQueries.Add($"{foundKeyword} evaluation");
        }

        // Add domain-specific terms
        followUpQueries.Add($"{originalKeywords} static analysis");
        followUpQueries.Add($"{originalKeywords} empirical study");
        followUpQueries.Add($"{originalKeywords} mining");

        return followUpQueries.Take(4).ToList();
    }

    private static double CalculateGitHubSDKRepoConfidence(
        GitHub.Models.FullRepository repo,
        string keywords,
        string searchQuery
    )
    {
        var confidence = 0.4; // Base confidence for GitHub SDK results

        var lowerKeywords = keywords.ToLowerInvariant();
        var lowerName = (repo.Name ?? "").ToLowerInvariant();
        var lowerDescription = (repo.Description ?? "").ToLowerInvariant();
        var lowerQuery = searchQuery.ToLowerInvariant();

        // Name matching bonus
        if (
            lowerName.Contains("bug")
            || lowerName.Contains("defect")
            || lowerName.Contains("issue")
        )
            confidence += 0.3;
        if (
            lowerName.Contains("list")
            || lowerName.Contains("dataset")
            || lowerName.Contains("collection")
        )
            confidence += 0.2;

        // Description matching bonus
        if (
            lowerDescription.Contains("bug")
            || lowerDescription.Contains("defect")
            || lowerDescription.Contains("vulnerability")
        )
            confidence += 0.25;
        if (
            lowerDescription.Contains("dataset")
            || lowerDescription.Contains("benchmark")
            || lowerDescription.Contains("evaluation")
        )
            confidence += 0.15;

        // Query relevance bonus
        var queryTerms = lowerQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matchingTerms = queryTerms.Count(term =>
            lowerName.Contains(term) || lowerDescription.Contains(term)
        );
        confidence += Math.Min(0.2, matchingTerms * 0.05);

        // Repository quality indicators
        var stars = repo.StargazersCount ?? 0;
        if (stars > 10)
            confidence += 0.05;
        if (stars > 100)
            confidence += 0.1;
        if (stars > 1000)
            confidence += 0.1;

        var hasReadme = !string.IsNullOrEmpty(repo.Description);
        if (hasReadme)
            confidence += 0.05;

        // Recent activity bonus
        var daysSinceUpdate = (
            DateTimeOffset.UtcNow - (repo.UpdatedAt ?? DateTimeOffset.MinValue)
        ).Days;
        if (daysSinceUpdate < 365)
            confidence += 0.05;
        if (daysSinceUpdate < 90)
            confidence += 0.05;

        return Math.Min(1.0, confidence);
    }

    private static List<string> ExtractKeywordsFromGitHubRepo(
        GitHub.Models.FullRepository repo,
        string searchKeywords
    )
    {
        var keywords = new List<string>();
        var text = $"{repo.Name} {repo.Description}".ToLowerInvariant();

        var targetKeywords = new[]
        {
            "bug",
            "defect",
            "issue",
            "vulnerability",
            "flaw",
            "error",
            "dataset",
            "benchmark",
            "evaluation",
            "study",
            "analysis",
            "tool",
            "detector",
            "finder",
            "analyzer",
            "scanner",
            "list",
            "collection",
            "repository",
            "database",
        };

        foreach (var keyword in targetKeywords)
        {
            if (text.Contains(keyword))
                keywords.Add(keyword);
        }

        // Add language if specified
        if (!string.IsNullOrEmpty(repo.Language))
            keywords.Add(repo.Language.ToLowerInvariant());

        return keywords.Distinct().ToList();
    }

    private static double CalculateGitHubRepoConfidence(WebSearchResult repo, string keywords)
    {
        var confidence = 0.5; // Base confidence for GitHub results

        var lowerKeywords = keywords.ToLowerInvariant();
        var lowerTitle = (repo.Title ?? "").ToLowerInvariant();
        var lowerSnippet = (repo.Snippet ?? "").ToLowerInvariant();

        // Title matching bonus
        if (
            lowerTitle.Contains("bug")
            || lowerTitle.Contains("defect")
            || lowerTitle.Contains("issue")
        )
            confidence += 0.3;

        // Snippet matching bonus
        if (
            lowerSnippet.Contains("bug")
            || lowerSnippet.Contains("defect")
            || lowerSnippet.Contains("vulnerability")
        )
            confidence += 0.2;

        // Repository structure bonus
        if (repo.Url.Split('/').Length >= 5) // github.com/user/repo format
            confidence += 0.1;

        return Math.Min(1.0, confidence);
    }

    private static List<string> ExtractKeywordsFromRepo(WebSearchResult repo, string searchKeywords)
    {
        var keywordsFound = new List<string>();
        var searchTerms = searchKeywords.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var term in searchTerms)
        {
            if (repo.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
                keywordsFound.Add(term);
            if (repo.Snippet.Contains(term, StringComparison.OrdinalIgnoreCase))
                keywordsFound.Add(term);
        }

        return keywordsFound.Distinct().ToList();
    }

    [RequiresUnreferencedCode("Required for dynamic type.")]
    [RequiresDynamicCode("Required for dynamic type")]
    private static double CalculateWebResultConfidence(dynamic result, string query)
    {
        double confidence = 0.0;
        string title = result.Title ?? "";
        string snippet = result.Snippet ?? "";

        // Basic keyword matching
        var queryTerms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var term in queryTerms)
        {
            if (title.Contains(term, StringComparison.OrdinalIgnoreCase))
                confidence += 0.2;
            if (snippet.Contains(term, StringComparison.OrdinalIgnoreCase))
                confidence += 0.1;
        }

        // Boost for specific patterns
        if (title.Contains("bug", StringComparison.OrdinalIgnoreCase))
            confidence += 0.1;
        if (snippet.Contains("bug report", StringComparison.OrdinalIgnoreCase))
            confidence += 0.15;

        // Check for common research artifact hosting sites
        string url = result.Url.ToString();
        if (url.Contains("github.com"))
            confidence += 0.1;
        if (url.Contains("zenodo.org"))
            confidence += 0.2;

        return Math.Min(1.0, confidence);
    }

    [RequiresUnreferencedCode("Required for dynamic type.")]
    [RequiresDynamicCode("Required for dynamic type")]
    private static List<string> ExtractKeywordsFromWebResult(dynamic result, string query)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string title = result.Title ?? "";
        string snippet = result.Snippet ?? "";
        var queryTerms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var term in queryTerms)
        {
            if (title.Contains(term, StringComparison.OrdinalIgnoreCase))
                keywords.Add(term);
            if (snippet.Contains(term, StringComparison.OrdinalIgnoreCase))
                keywords.Add(term);
        }

        // Add other relevant keywords
        if (title.Contains("bug", StringComparison.OrdinalIgnoreCase))
            keywords.Add("bug");
        if (snippet.Contains("vulnerability", StringComparison.OrdinalIgnoreCase))
            keywords.Add("vulnerability");

        return keywords.ToList();
    }

    [RequiresUnreferencedCode("Required for dynamic type.")]
    [RequiresDynamicCode("Required for dynamic type.")]
    private static double CalculateZenodoConfidence(dynamic result, string query)
    {
        double confidence = 0.0;
        var lowerTitle = (result.Title ?? "").ToLowerInvariant();
        var lowerDescription = (result.Snippet ?? "").ToLowerInvariant();

        // Research artifact indicators
        if (
            lowerTitle.Contains("dataset")
            || lowerTitle.Contains("artifact")
            || lowerTitle.Contains("benchmark")
        )
            confidence += 0.2;

        if (
            lowerDescription.Contains("bug")
            || lowerDescription.Contains("defect")
            || lowerDescription.Contains("vulnerability")
        )
            confidence += 0.2;

        return Math.Min(1.0, confidence);
    }
}
