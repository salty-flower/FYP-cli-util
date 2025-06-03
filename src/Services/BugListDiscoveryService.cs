using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DataCollection.Models;
using DataCollection.Models.Export.BugAnalysis;
using DataCollection.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using OpenAI;
using OpenAI.Chat;
using OpenAi.JsonSchema.Generator;
using OpenAi.JsonSchema.Serialization;

namespace DataCollection.Services;

/// <summary>
/// Service for discovering bug lists and artifact repositories from papers
/// </summary>
public class BugListDiscoveryService(
    IWebSearchService webSearchService,
    DataLoadingService dataLoadingService,
    GitHubService gitHubService,
    ILogger<BugListDiscoveryService> logger,
    IOptions<PathsOptions> pathsOptions,
    IOptions<CredentialOptions> credentialOptions,
    Kernel kernel
)
{
    private const int MaxSearchAttempts = 3;
    private static readonly string[] BugTrackingPatterns =
    [
        @"github\.com/[^/]+/[^/]+/issues",
        @"jira\.[^/]+/browse/",
        @"bugzilla\.[^/]+/show_bug",
        @"issues\.apache\.org/jira/browse/",
        @"bugs\.launchpad\.net/",
        @"tracker\.debian\.org/pkg/",
        @"sourceforge\.net/p/[^/]+/bugs/",
    ];

    private static readonly string[] RepositoryPatterns =
    [
        @"github\.com/[^/]+/[^/]+/?$",
        @"gitlab\.com/[^/]+/[^/]+/?$",
        @"bitbucket\.org/[^/]+/[^/]+/?$",
        @"sourceforge\.net/projects/[^/]+/?$",
        @"zenodo\.org/record/\d+/?$",
        @"zenodo\.org/doi/[^/\s]+/?$",
        @"figshare\.com/articles/[^/\s]+/?$",
        @"figshare\.com/s/[^/\s]+/?$",
        @"osf\.io/[^/\s]+/?$",
        @"ieee-dataport\.org/[^/\s]+/?$",
        @"researchgate\.net/[^/\s]+/?$",
        @"archive\.org/details/[^/\s]+/?$",
    ];

    private static readonly string[] ArtifactKeywords =
    [
        "artifact",
        "artifacts",
        "replication package",
        "reproduction package",
        "supplementary material",
        "source code",
        "implementation",
        "available at",
        "code repository",
        "data repository",
        "zenodo",
        "figshare",
        "osf.io",
        "github.com",
        "gitlab.com",
        "bitbucket.org",
        "doi.org",
        "dataset",
        "experimental data",
        "benchmark",
        "tool download",
    ];

    public async Task<BugListDiscoveryAnalysis> DiscoverBugListsAsync(
        List<string> dois,
        CancellationToken cancellationToken = default
    )
    {
        logger.LogInformation("Starting bug list discovery for {Count} DOIs", dois.Count);

        var papers = dataLoadingService.LoadPapersFromMetadata(pathsOptions.Value.PaperMetadataDir);
        var targetPapers = papers.Where(p => dois.Contains(p.Doi)).ToList();

        logger.LogInformation(
            "Found {Count} papers matching the provided DOIs",
            targetPapers.Count
        );

        var results = new List<BugListDiscoveryResult>();
        var totalSearchAttempts = 0;

        foreach (var paper in targetPapers)
        {
            var result = await DiscoverBugListsForPaperAsync(paper, cancellationToken);
            results.Add(result);
            totalSearchAttempts += result.SearchAttempts;

            logger.LogInformation(
                "Processed paper {Title} - Bug lists: {BugLists}, Artifacts: {Artifacts}",
                paper.Title,
                result.BugLists.Count,
                result.ArtifactRepositories.Count
            );
        }

        return CreateAnalysis(results, totalSearchAttempts);
    }

    /// <summary>
    /// Discover bug lists for a single paper
    /// </summary>
    private async Task<BugListDiscoveryResult> DiscoverBugListsForPaperAsync(
        Paper paper,
        CancellationToken cancellationToken = default
    )
    {
        var result = new BugListDiscoveryResult
        {
            Doi = paper.Doi,
            Title = paper.Title,
            SearchAttempts = 0,
        };

        try
        {
            await ExtractFromPdfContent(paper, result, cancellationToken);
            await SearchForArtifacts(paper, result, cancellationToken);
            result.DiscoverySuccessful = result.BugLists.Any() || result.ArtifactRepositories.Any();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during bug list discovery for paper {Title}", paper.Title);
            result.ErrorMessage = ex.Message;
            result.DiscoverySuccessful = false;
        }

        return result;
    }

    /// <summary>
    /// Extract bug tracking URLs and repositories from PDF content
    /// </summary>
    private async Task ExtractFromPdfContent(
        Paper paper,
        BugListDiscoveryResult result,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var pdfData = dataLoadingService.LoadPdfData(
                pathsOptions.Value.PdfDataDir,
                paper.SanitizedDoi
            );
            if (pdfData == null)
            {
                logger.LogWarning("No PDF data found for paper {Title}", paper.Title);
                return;
            }

            var fullText = string.Join(" ", pdfData.Texts);

            ExtractBugTrackingUrls(fullText, result);
            ExtractRepositoryUrls(fullText, result);
            await ExtractArtifactsByKeywords(fullText, result, paper, cancellationToken);
            await AnalyzeTextWithLLM(paper, fullText, result, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not extract from PDF content for paper {Title}",
                paper.Title
            );
        }
    }

    /// <summary>
    /// Extract bug tracking URLs using regex patterns
    /// </summary>
    private void ExtractBugTrackingUrls(string text, BugListDiscoveryResult result)
    {
        foreach (var pattern in BugTrackingPatterns)
        {
            var matches = Regex.Matches(text, pattern, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                var url = match.Value;
                if (!url.StartsWith("http"))
                    url = "https://" + url;

                var type = GetBugTrackingType(url);
                result.BugLists.Add(
                    new BugListSource
                    {
                        Url = url,
                        Type = type,
                        DiscoveryMethod = "PDF Text Analysis",
                        Confidence = 0.9,
                    }
                );
            }
        }
    }

    /// <summary>
    /// Extract repository URLs using regex patterns
    /// </summary>
    private void ExtractRepositoryUrls(string text, BugListDiscoveryResult result)
    {
        foreach (var pattern in RepositoryPatterns)
        {
            var matches = Regex.Matches(text, pattern, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                var url = match.Value;
                if (!url.StartsWith("http"))
                    url = "https://" + url;

                var type = GetRepositoryType(url);
                result.ArtifactRepositories.Add(
                    new ArtifactRepository
                    {
                        Url = url,
                        Type = type,
                        DiscoveryMethod = "PDF Text Analysis",
                        Confidence = 0.9,
                    }
                );
            }
        }
    }

    /// <summary>
    /// Extract artifact links using keyword-based search (fast initial pass)
    /// </summary>
    private async Task ExtractArtifactsByKeywords(
        string text,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        // Normalize text by removing line breaks within URLs and sentences
        var normalizedText = text.Replace("\n", " ").Replace("\r", " ");

        // Look for specific sections that mention artifacts
        var artifactSections = new[]
        {
            "data availability",
            "artifact availability",
            "code availability",
            "replication package",
            "supplementary material",
            "source code",
            "implementation",
        };

        var lowerText = normalizedText.ToLowerInvariant();
        logger.LogDebug(
            "Searching for artifacts in text of length {Length}",
            normalizedText.Length
        );

        // Check if any artifact section exists
        bool hasArtifactSection = artifactSections.Any(section => lowerText.Contains(section));

        if (hasArtifactSection)
        {
            logger.LogInformation("Found artifact-related section in PDF text");

            // Extract all URLs from the entire text when artifact sections are found
            var urlPattern = @"https?://[^\s\)]+";
            var matches = Regex.Matches(normalizedText, urlPattern, RegexOptions.IgnoreCase);

            logger.LogDebug("Found {Count} potential URLs in text", matches.Count);

            foreach (Match match in matches)
            {
                var url = match.Value.TrimEnd(',', '.', ')', ']', '}', ';');
                logger.LogDebug("Processing URL: {Url}", url);

                // Check if it's a repository URL
                if (
                    RepositoryPatterns.Any(pattern =>
                        Regex.IsMatch(url, pattern, RegexOptions.IgnoreCase)
                    )
                )
                {
                    var type = GetRepositoryType(url);
                    var existingRepo = result.ArtifactRepositories.FirstOrDefault(r =>
                        r.Url == url
                    );

                    if (existingRepo == null)
                    {
                        // Get context around the URL for LLM verification
                        var urlIndex = normalizedText.IndexOf(
                            url,
                            StringComparison.OrdinalIgnoreCase
                        );
                        var contextStart = Math.Max(0, urlIndex - 200);
                        var contextEnd = Math.Min(
                            normalizedText.Length,
                            urlIndex + url.Length + 200
                        );
                        var context = normalizedText.Substring(
                            contextStart,
                            contextEnd - contextStart
                        );

                        // Verify with LLM using context
                        var (isValid, confidence, reasoning) = await VerifyArtifactWithContext(
                            paper,
                            url,
                            context,
                            cancellationToken
                        );

                        if (isValid && confidence >= 0.3)
                        {
                            result.ArtifactRepositories.Add(
                                new ArtifactRepository
                                {
                                    Url = url,
                                    Type = type,
                                    DiscoveryMethod = "PDF Keyword Analysis + LLM Verification",
                                    Confidence = Math.Min(confidence * 0.9, 1.0), // Slightly lower than pure keyword
                                }
                            );

                            logger.LogInformation(
                                "Verified artifact repository via keywords: {Url} - {Reasoning}",
                                url,
                                reasoning
                            );
                        }
                        else
                        {
                            logger.LogDebug(
                                "Rejected keyword-found URL {Url}: {Reasoning}",
                                url,
                                reasoning
                            );
                        }
                    }
                }
                // Check if it's a bug tracking URL
                else if (
                    BugTrackingPatterns.Any(pattern =>
                        Regex.IsMatch(url, pattern, RegexOptions.IgnoreCase)
                    )
                )
                {
                    var type = GetBugTrackingType(url);
                    var existingBugList = result.BugLists.FirstOrDefault(b => b.Url == url);

                    if (existingBugList == null)
                    {
                        result.BugLists.Add(
                            new BugListSource
                            {
                                Url = url,
                                Type = type,
                                DiscoveryMethod = "PDF Keyword Analysis",
                                Confidence = 0.95, // High confidence for explicit mentions
                            }
                        );

                        logger.LogInformation("Found bug tracking URL via keywords: {Url}", url);
                    }
                }
            }
        }
        else
        {
            logger.LogDebug("No artifact-related sections found in PDF text");
        }

        // Also do the original sentence-by-sentence analysis for other artifact keywords
        var sentences = normalizedText.Split(['.', '\n'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var sentence in sentences)
        {
            var lowerSentence = sentence.ToLowerInvariant();

            // Check if sentence contains artifact keywords
            if (ArtifactKeywords.Any(keyword => lowerSentence.Contains(keyword.ToLowerInvariant())))
            {
                // Look for URLs in this sentence
                var urlPattern = @"https?://[^\s\)]+";
                var matches = Regex.Matches(sentence, urlPattern, RegexOptions.IgnoreCase);

                foreach (Match match in matches)
                {
                    var url = match.Value.TrimEnd(',', '.', ')', ']', '}', ';');

                    // Check if it's a repository URL
                    if (
                        RepositoryPatterns.Any(pattern =>
                            Regex.IsMatch(url, pattern, RegexOptions.IgnoreCase)
                        )
                    )
                    {
                        var type = GetRepositoryType(url);
                        var existingRepo = result.ArtifactRepositories.FirstOrDefault(r =>
                            r.Url == url
                        );

                        if (existingRepo == null)
                        {
                            result.ArtifactRepositories.Add(
                                new ArtifactRepository
                                {
                                    Url = url,
                                    Type = type,
                                    DiscoveryMethod = "PDF Keyword Analysis",
                                    Confidence = 0.95, // High confidence for explicit mentions
                                }
                            );

                            logger.LogInformation(
                                "Found artifact repository via sentence keywords: {Url}",
                                url
                            );
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Use LLM to analyze text for potential artifacts
    /// </summary>
    private async Task AnalyzeTextWithLLM(
        Paper paper,
        string fullText,
        BugListDiscoveryResult result,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var request = new BugListAnalysisRequest(
                paper.Title,
                fullText.Substring(0, Math.Min(fullText.Length, 4000))
            );

            var schema = new DefaultSchemaGenerator()
                .Generate<BugListAnalysisResponse>(new JsonSchemaOptions())
                .ToJson();

            var messages = new ChatMessage[]
            {
                new SystemChatMessage(
                    "Analyze research paper text and identify any mentions of bug tracking systems, issue trackers, software repositories, or project websites. Look for indirect references like 'Our code is available at...', 'Issues can be reported at...', 'The implementation can be found...', repository names without full URLs, or project names that might have public repositories."
                ),
                new UserChatMessage(
                    $"Paper Title: {request.PaperTitle}\n\nText: {request.PaperText}"
                ),
            };

            var options = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: "bug-list-analysis",
                    jsonSchema: BinaryData.FromString(schema),
                    jsonSchemaIsStrict: true
                ),
            };

            var chatClient = kernel.GetRequiredService<ChatClient>();
            var response = await chatClient.CompleteChatAsync(messages, options, cancellationToken);

            if (response.Value.Content.Count == 0)
                return;

            var result_text = response.Value.Content[0].Text;
            if (string.IsNullOrEmpty(result_text))
                return;

            var analysisResponse = JsonSerializer.Deserialize<BugListAnalysisResponse>(
                result_text,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            if (analysisResponse?.Mentions != null)
            {
                await ProcessLLMResponse(analysisResponse, result, paper, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not analyze text with LLM for paper {Title}", paper.Title);
        }
    }

    /// <summary>
    /// Process LLM response and search for mentioned projects
    /// </summary>
    private async Task ProcessLLMResponse(
        BugListAnalysisResponse response,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        foreach (var mention in response.Mentions)
        {
            if (!string.IsNullOrWhiteSpace(mention.ProjectName))
            {
                await SearchForProject(
                    mention.ProjectName,
                    mention,
                    result,
                    paper,
                    cancellationToken
                );
            }
        }
    }

    /// <summary>
    /// Search for artifacts online using web search
    /// </summary>
    private async Task SearchForArtifacts(
        Paper paper,
        BugListDiscoveryResult result,
        CancellationToken cancellationToken
    )
    {
        // Skip expensive web search if we already found high-confidence artifacts
        var highConfidenceArtifacts = result
            .ArtifactRepositories.Where(a => a.Confidence >= 0.7)
            .ToList();
        if (highConfidenceArtifacts.Any())
        {
            logger.LogInformation(
                "Found {Count} high-confidence artifacts via keyword analysis (confidence >= 0.7), skipping web search",
                highConfidenceArtifacts.Count
            );
            foreach (var artifact in highConfidenceArtifacts)
            {
                logger.LogDebug(
                    "High-confidence artifact: {Url} (confidence: {Confidence})",
                    artifact.Url,
                    artifact.Confidence
                );
            }
            return;
        }

        logger.LogDebug(
            "No high-confidence artifacts found (total artifacts: {Count}), proceeding with web search",
            result.ArtifactRepositories.Count
        );

        var searchQueries = GenerateSearchQueries(paper);

        foreach (var query in searchQueries.Take(MaxSearchAttempts))
        {
            result.SearchAttempts++;
            logger.LogInformation("Searching for artifacts with query: {Query}", query);

            var searchResults = await webSearchService.SearchAsync(query, 10, cancellationToken);

            foreach (var searchResult in searchResults)
            {
                await ProcessSearchResultAsync(searchResult, result, paper, cancellationToken);
            }

            if (result.BugLists.Any() || result.ArtifactRepositories.Any())
            {
                break;
            }
        }
    }

    /// <summary>
    /// Search for a specific project mentioned in the paper
    /// </summary>
    private async Task SearchForProject(
        string projectName,
        ArtifactMention mention,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        var query = $"{projectName} github repository";
        var searchResults = await webSearchService.SearchAsync(query, 5, cancellationToken);

        foreach (var searchResult in searchResults)
        {
            if (
                searchResult.Url.Contains("github.com")
                || searchResult.Url.Contains("gitlab.com")
                || searchResult.Url.Contains("bitbucket.org")
                || searchResult.Url.Contains("zenodo.org")
                || searchResult.Url.Contains("figshare.com")
                || searchResult.Url.Contains("osf.io")
            )
            {
                // Verify if this is actually an artifact repository for the paper
                var (isValid, confidence, reasoning) = await VerifyRepositoryAsync(
                    paper,
                    searchResult.Url,
                    cancellationToken
                );

                if (isValid && confidence >= 0.3)
                {
                    var type = GetRepositoryType(searchResult.Url);
                    result.ArtifactRepositories.Add(
                        new ArtifactRepository
                        {
                            Url = searchResult.Url,
                            Type = type,
                            DiscoveryMethod =
                                $"LLM Analysis + Web Search + Verification ({projectName})",
                            Confidence = Math.Min(mention.Confidence * confidence * 0.7, 1.0),
                        }
                    );

                    logger.LogInformation(
                        "Verified project repository {Url} for {ProjectName}: {Reasoning}",
                        searchResult.Url,
                        projectName,
                        reasoning
                    );
                }
                else
                {
                    logger.LogDebug(
                        "Project repository {Url} rejected for {ProjectName}: {Reasoning}",
                        searchResult.Url,
                        projectName,
                        reasoning
                    );
                }
            }
        }
    }

    /// <summary>
    /// Generate search queries for finding artifacts
    /// </summary>
    private List<string> GenerateSearchQueries(Paper paper)
    {
        var queries = new List<string>();
        var firstAuthor = paper.Authors.FirstOrDefault()?.Split(' ').LastOrDefault() ?? "";

        var titleWords = paper
            .Title.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3 && char.IsUpper(w[0]))
            .Take(3);

        foreach (var word in titleWords)
        {
            queries.Add($"{word} {firstAuthor} github repository");
            queries.Add($"{word} {firstAuthor} zenodo");
            queries.Add($"{word} {firstAuthor} figshare");
            queries.Add($"{word} source code implementation");
            queries.Add($"{word} replication package");
        }

        // Add general paper searches
        queries.Add($"\"{paper.Title}\" artifact repository");
        queries.Add($"\"{paper.Title}\" replication package");
        queries.Add($"\"{paper.Title}\" zenodo figshare");
        queries.Add($"{firstAuthor} {DateTime.Now.Year} software repository");
        queries.Add($"{firstAuthor} {DateTime.Now.Year} artifact doi");

        return queries;
    }

    /// <summary>
    /// Process search result and extract relevant URLs with verification
    /// </summary>
    private async Task ProcessSearchResultAsync(
        WebSearchResult searchResult,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        foreach (var pattern in RepositoryPatterns)
        {
            if (Regex.IsMatch(searchResult.Url, pattern, RegexOptions.IgnoreCase))
            {
                // Verify if this is actually an artifact repository for the paper
                var (isValid, confidence, reasoning) = await VerifyRepositoryAsync(
                    paper,
                    searchResult.Url,
                    cancellationToken
                );

                if (isValid && confidence >= 0.3) // Only add repositories with reasonable confidence
                {
                    var type = GetRepositoryType(searchResult.Url);
                    result.ArtifactRepositories.Add(
                        new ArtifactRepository
                        {
                            Url = searchResult.Url,
                            Type = type,
                            DiscoveryMethod = "Web Search + LLM Verification",
                            Confidence = confidence * 0.6, // Reduce confidence for web search results
                        }
                    );

                    logger.LogInformation(
                        "Verified repository {Url} for paper {Title}: {Reasoning}",
                        searchResult.Url,
                        paper.Title,
                        reasoning
                    );
                }
                else
                {
                    logger.LogDebug(
                        "Repository {Url} rejected for paper {Title}: {Reasoning}",
                        searchResult.Url,
                        paper.Title,
                        reasoning
                    );
                }
                return;
            }
        }

        foreach (var pattern in BugTrackingPatterns)
        {
            if (Regex.IsMatch(searchResult.Url, pattern, RegexOptions.IgnoreCase))
            {
                var type = GetBugTrackingType(searchResult.Url);
                result.BugLists.Add(
                    new BugListSource
                    {
                        Url = searchResult.Url,
                        Type = type,
                        DiscoveryMethod = "Web Search",
                        Confidence = 0.6,
                    }
                );
                return;
            }
        }
    }

    /// <summary>
    /// Determine bug tracking system type from URL
    /// </summary>
    private string GetBugTrackingType(string url)
    {
        if (url.Contains("github.com"))
            return "GitHub Issues";
        if (url.Contains("jira"))
            return "Jira";
        if (url.Contains("bugzilla"))
            return "Bugzilla";
        if (url.Contains("launchpad"))
            return "Launchpad";
        if (url.Contains("sourceforge"))
            return "SourceForge";
        return "Unknown";
    }

    /// <summary>
    /// Determine repository type from URL
    /// </summary>
    private string GetRepositoryType(string url)
    {
        if (url.Contains("github.com"))
            return "GitHub";
        if (url.Contains("gitlab.com"))
            return "GitLab";
        if (url.Contains("bitbucket.org"))
            return "Bitbucket";
        if (url.Contains("sourceforge.net"))
            return "SourceForge";
        if (url.Contains("zenodo.org"))
            return "Zenodo";
        if (url.Contains("figshare.com"))
            return "Figshare";
        if (url.Contains("osf.io"))
            return "OSF";
        if (url.Contains("ieee-dataport.org"))
            return "IEEE DataPort";
        if (url.Contains("researchgate.net"))
            return "ResearchGate";
        if (url.Contains("archive.org"))
            return "Internet Archive";
        if (url.Contains("doi.org"))
            return "DOI";
        return "Unknown";
    }

    /// <summary>
    /// Create analysis summary from individual results
    /// </summary>
    private BugListDiscoveryAnalysis CreateAnalysis(
        List<BugListDiscoveryResult> results,
        int totalSearchAttempts
    )
    {
        var bugTrackingStats = new Dictionary<string, int>();
        var repositoryStats = new Dictionary<string, int>();

        foreach (var result in results)
        {
            foreach (var bugList in result.BugLists)
            {
                bugTrackingStats[bugList.Type] =
                    bugTrackingStats.GetValueOrDefault(bugList.Type) + 1;
            }

            foreach (var repo in result.ArtifactRepositories)
            {
                repositoryStats[repo.Type] = repositoryStats.GetValueOrDefault(repo.Type) + 1;
            }
        }

        return new BugListDiscoveryAnalysis
        {
            Summary = new BugListDiscoverySummary
            {
                TotalPapers = results.Count,
                PapersWithBugLists = results.Count(r => r.BugLists.Any()),
                PapersWithArtifacts = results.Count(r => r.ArtifactRepositories.Any()),
                FailedDiscoveries = results.Count(r => !r.DiscoverySuccessful),
                TotalSearchAttempts = totalSearchAttempts,
            },
            PaperResults = results,
            BugTrackingSystemStats = bugTrackingStats,
            RepositoryTypeStats = repositoryStats,
        };
    }

    /// <summary>
    /// Verify if a repository is actually an artifact repository for the paper
    /// </summary>
    private async Task<(bool isValid, double confidence, string reasoning)> VerifyRepositoryAsync(
        Paper paper,
        string repositoryUrl,
        CancellationToken cancellationToken
    )
    {
        try
        {
            // Extract owner and repo name from GitHub URL
            var match = Regex.Match(repositoryUrl, @"github\.com/([^/]+)/([^/]+)/?$");
            if (!match.Success)
            {
                logger.LogDebug(
                    "Non-GitHub repository, skipping verification: {Url}",
                    repositoryUrl
                );
                return (true, 0.5, "Non-GitHub repository - cannot verify");
            }

            var owner = match.Groups[1].Value;
            var repoName = match.Groups[2].Value;

            // Get repository info and README
            var repoInfo = await gitHubService.GetRepositoryInfoAsync(owner, repoName);
            var readmeContent = await gitHubService.GetRepositoryReadmeAsync(owner, repoName);

            if (string.IsNullOrEmpty(readmeContent))
            {
                logger.LogDebug(
                    "No README found for {Owner}/{RepoName}, using basic verification",
                    owner,
                    repoName
                );
                readmeContent = "No README available";
            }

            // Prepare verification request
            var request = new RepositoryVerificationRequest(
                paper.Title,
                string.Join(", ", paper.Authors),
                repoInfo.FullName ?? $"{owner}/{repoName}",
                repoInfo.Description ?? "",
                readmeContent.Length > 3000
                    ? readmeContent.Substring(0, 3000) + "..."
                    : readmeContent
            );

            var schema = new DefaultSchemaGenerator()
                .Generate<RepositoryVerificationResponse>(new JsonSchemaOptions())
                .ToJson();

            var messages = new ChatMessage[]
            {
                new SystemChatMessage(
                    "You are tasked with determining if a GitHub repository is an artifact repository for a specific research paper. "
                        + "An artifact repository should contain the actual implementation, data, or tools described in the paper, "
                        + "not just a collection of papers or general-purpose tools. "
                        + "Look for evidence that this repository specifically implements or supports the research described in the paper. "
                        + "Consider repository name, description, README content, and whether it matches the paper's focus."
                ),
                new UserChatMessage(
                    $"Paper Title: {request.PaperTitle}\n"
                        + $"Authors: {request.PaperAuthors}\n"
                        + $"Repository: {request.RepositoryName}\n"
                        + $"Description: {request.RepositoryDescription}\n"
                        + $"README Content: {request.ReadmeContent}"
                ),
            };

            var options = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: "repository-verification",
                    jsonSchema: BinaryData.FromString(schema),
                    jsonSchemaIsStrict: true
                ),
            };

            // Use SMALL model for verification
            var smallModelClient = kernel
                .GetRequiredService<OpenAIClient>()
                .GetChatClient(credentialOptions.Value.SmallModel);
            var response = await smallModelClient.CompleteChatAsync(
                messages,
                options,
                cancellationToken
            );

            if (response.Value.Content.Count == 0)
                return (false, 0.1, "No response from verification");

            var resultText = response.Value.Content[0].Text;
            if (string.IsNullOrEmpty(resultText))
                return (false, 0.1, "Empty response from verification");

            var verificationResponse = JsonSerializer.Deserialize<RepositoryVerificationResponse>(
                resultText,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            if (verificationResponse != null)
            {
                logger.LogDebug(
                    "Repository verification for {Repo}: {IsValid} (confidence: {Confidence}) - {Reasoning}",
                    repositoryUrl,
                    verificationResponse.IsArtifactRepository,
                    verificationResponse.Confidence,
                    verificationResponse.Reasoning
                );

                return (
                    verificationResponse.IsArtifactRepository,
                    verificationResponse.Confidence,
                    verificationResponse.Reasoning
                );
            }

            return (false, 0.1, "Failed to parse verification response");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error verifying repository {Url}", repositoryUrl);
            return (false, 0.1, $"Verification error: {ex.Message}");
        }
    }

    /// <summary>
    /// Verify if a URL is actually an artifact for the paper
    /// </summary>
    private async Task<(
        bool isValid,
        double confidence,
        string reasoning
    )> VerifyArtifactWithContext(
        Paper paper,
        string url,
        string context,
        CancellationToken cancellationToken
    )
    {
        try
        {
            // Prepare verification request
            var request = new ArtifactVerificationRequest(
                paper.Title,
                string.Join(", ", paper.Authors),
                url,
                context
            );

            var schema = new DefaultSchemaGenerator()
                .Generate<ArtifactVerificationResponse>(new JsonSchemaOptions())
                .ToJson();

            var messages = new ChatMessage[]
            {
                new SystemChatMessage(
                    "You are tasked with determining if a URL is an artifact for a specific research paper. "
                        + "An artifact should contain the actual implementation, data, or tools described in the paper, "
                        + "not just a collection of papers or general-purpose tools. "
                        + "Look for evidence that this URL specifically implements or supports the research described in the paper. "
                        + "Consider the URL itself, the context around the URL, and whether it matches the paper's focus."
                ),
                new UserChatMessage(
                    $"Paper Title: {request.PaperTitle}\n"
                        + $"Authors: {request.PaperAuthors}\n"
                        + $"URL: {request.Url}\n"
                        + $"Context: {request.Context}"
                ),
            };

            var options = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: "artifact-verification",
                    jsonSchema: BinaryData.FromString(schema),
                    jsonSchemaIsStrict: true
                ),
            };

            // Use SMALL model for verification
            var smallModelClient = kernel
                .GetRequiredService<OpenAIClient>()
                .GetChatClient(credentialOptions.Value.SmallModel);
            var response = await smallModelClient.CompleteChatAsync(
                messages,
                options,
                cancellationToken
            );

            if (response.Value.Content.Count == 0)
                return (false, 0.1, "No response from verification");

            var resultText = response.Value.Content[0].Text;
            if (string.IsNullOrEmpty(resultText))
                return (false, 0.1, "Empty response from verification");

            var verificationResponse = JsonSerializer.Deserialize<ArtifactVerificationResponse>(
                resultText,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            if (verificationResponse != null)
            {
                logger.LogDebug(
                    "Artifact verification for {Url}: {IsValid} (confidence: {Confidence}) - {Reasoning}",
                    url,
                    verificationResponse.IsArtifact,
                    verificationResponse.Confidence,
                    verificationResponse.Reasoning
                );

                return (
                    verificationResponse.IsArtifact,
                    verificationResponse.Confidence,
                    verificationResponse.Reasoning
                );
            }

            return (false, 0.1, "Failed to parse verification response");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error verifying artifact {Url}", url);
            return (false, 0.1, $"Verification error: {ex.Message}");
        }
    }
}

/// <summary>
/// LLM analysis response model
/// </summary>
public class LLMAnalysisResponse
{
    public List<LLMMention> Mentions { get; set; } = new();
}

/// <summary>
/// LLM mention model
/// </summary>
public class LLMMention
{
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public double Confidence { get; set; }
}
