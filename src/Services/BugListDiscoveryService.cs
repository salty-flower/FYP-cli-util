using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DataCollection.Commands;
using DataCollection.Models;
using DataCollection.Models.Export.BugAnalysis;
using DataCollection.Options;
using DataCollection.Utils;
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
public partial class BugListDiscoveryService(
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
    private const int MaxTextLengthForLlm = 4000;
    private const int MaxReadmeLength = 3000;
    private const int ContextWindowSize = 200;
    private const double HighConfidenceThreshold = 0.7;
    private const double MinAcceptableConfidence = 0.3;
    private const double PdfAnalysisConfidence = 0.9;
    private const double KeywordAnalysisConfidence = 0.95;
    private const double WebSearchConfidenceMultiplier = 0.6;
    private const double LlmVerificationMultiplier = 0.7;
    private const double KeywordLlmConfidenceMultiplier = 0.9;

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

    private static readonly string[] ArtifactSections =
    [
        "data availability",
        "artifact availability",
        "code availability",
        "replication package",
        "supplementary material",
        "source code",
        "implementation",
    ];

    public async Task<BugListDiscoveryAnalysis> DiscoverBugListsAsync(
        List<string> dois,
        CancellationToken cancellationToken = default
    )
    {
        logger.LogInformation("Starting bug list discovery for {Count} DOIs", dois.Count);

        var papers = LoadTargetPapers(dois);
        var results = await ProcessPapers(papers, cancellationToken);

        return CreateAnalysisFromResults(results);
    }

    private List<Paper> LoadTargetPapers(List<string> dois)
    {
        var papers = dataLoadingService.LoadPapersFromMetadata(pathsOptions.Value.PaperMetadataDir);
        var targetPapers = papers.Where(p => dois.Contains(p.Doi)).ToList();

        logger.LogInformation(
            "Found {Count} papers matching the provided DOIs",
            targetPapers.Count
        );
        return targetPapers;
    }

    private async Task<List<BugListDiscoveryResult>> ProcessPapers(
        List<Paper> papers,
        CancellationToken cancellationToken
    )
    {
        var results = new List<BugListDiscoveryResult>();

        foreach (var paper in papers)
        {
            var result = await ProcessSinglePaper(paper, cancellationToken);
            results.Add(result);

            LogPaperProcessingResult(paper, result);
        }

        return results;
    }

    private async Task<BugListDiscoveryResult> ProcessSinglePaper(
        Paper paper,
        CancellationToken cancellationToken
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
            await SearchForArtifactsIfNeeded(paper, result, cancellationToken);
            result.DiscoverySuccessful = HasFoundArtifacts(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during bug list discovery for paper {Title}", paper.Title);
            result.ErrorMessage = ex.Message;
            result.DiscoverySuccessful = false;
        }

        return result;
    }

    private void LogPaperProcessingResult(Paper paper, BugListDiscoveryResult result)
    {
        logger.LogInformation(
            "Processed paper {Title} - Bug lists: {BugLists}, Artifacts: {Artifacts}",
            paper.Title,
            result.BugLists.Count,
            result.ArtifactRepositories.Count
        );
    }

    private static bool HasFoundArtifacts(BugListDiscoveryResult result) =>
        result.BugLists.Count != 0 || result.ArtifactRepositories.Count != 0;

    private async Task ExtractFromPdfContent(
        Paper paper,
        BugListDiscoveryResult result,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var pdfData = LoadPdfData(paper);
            if (pdfData == null)
                return;

            var fullText = string.Join(" ", pdfData.Texts);

            ExtractDirectUrls(fullText, result);
            await ExtractArtifactsByKeywords(fullText, result, paper, cancellationToken);
            await AnalyzeTextWithLlm(paper, fullText, result, cancellationToken);
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

    private PdfData? LoadPdfData(Paper paper)
    {
        var pdfData = dataLoadingService.LoadPdfData(
            pathsOptions.Value.PdfDataDir,
            paper.SanitizedDoi
        );

        if (pdfData == null)
        {
            logger.LogWarning("No PDF data found for paper {Title}", paper.Title);
        }

        return pdfData;
    }

    private void ExtractDirectUrls(string text, BugListDiscoveryResult result)
    {
        ExtractBugTrackingUrls(text, result);
        ExtractRepositoryUrls(text, result);
    }

    private void ExtractBugTrackingUrls(string text, BugListDiscoveryResult result)
    {
        foreach (var pattern in BugTrackingPatterns)
        {
            var matches = Regex.Matches(text, pattern, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                var url = EnsureHttpsUrl(match.Value);
                var type = GetBugTrackingType(url);

                result.BugLists.Add(
                    new BugListSource
                    {
                        Url = url,
                        Type = type,
                        DiscoveryMethod = "PDF Text Analysis",
                        Confidence = PdfAnalysisConfidence,
                    }
                );
            }
        }
    }

    private void ExtractRepositoryUrls(string text, BugListDiscoveryResult result)
    {
        foreach (var pattern in RepositoryPatterns)
        {
            var matches = Regex.Matches(text, pattern, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                var url = EnsureHttpsUrl(match.Value);
                var type = GetRepositoryType(url);

                result.ArtifactRepositories.Add(
                    new ArtifactRepository
                    {
                        Url = url,
                        Type = type,
                        DiscoveryMethod = "PDF Text Analysis",
                        Confidence = PdfAnalysisConfidence,
                    }
                );
            }
        }
    }

    private static string EnsureHttpsUrl(string url) =>
        url.StartsWith("http") ? url : "https://" + url;

    private async Task ExtractArtifactsByKeywords(
        string text,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        var normalizedText = text.RemoveLineEndings();

        await ProcessArtifactSections(normalizedText, result, paper, cancellationToken);
        ProcessKeywordSentences(normalizedText, result);
    }

    private async Task ProcessArtifactSections(
        string normalizedText,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        var lowerText = normalizedText.ToLowerInvariant();
        var hasArtifactSection = ArtifactSections.Any(lowerText.Contains);

        if (!hasArtifactSection)
        {
            logger.LogDebug("No artifact-related sections found in PDF text");
            return;
        }

        logger.LogInformation("Found artifact-related section in PDF text");
        await ProcessUrlsInText(normalizedText, result, paper, cancellationToken);
    }

    private async Task ProcessUrlsInText(
        string text,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        var urlPattern = @"https?://[^\s\)]+";
        var matches = Regex.Matches(text, urlPattern, RegexOptions.IgnoreCase);

        logger.LogDebug("Found {Count} potential URLs in text", matches.Count);

        foreach (Match match in matches)
        {
            var url = CleanUrl(match.Value);
            await ProcessFoundUrl(url, text, result, paper, cancellationToken);
        }
    }

    private static string CleanUrl(string url)
    {
        return url.TrimEnd(',', '.', ')', ']', '}', ';');
    }

    private async Task ProcessFoundUrl(
        string url,
        string fullText,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        logger.LogDebug("Processing URL: {Url}", url);

        if (IsRepositoryUrl(url))
        {
            await ProcessRepositoryUrl(url, fullText, result, paper, cancellationToken);
        }
        else if (IsBugTrackingUrl(url))
        {
            ProcessBugTrackingUrl(url, result);
        }
    }

    private static bool IsRepositoryUrl(string url)
    {
        return RepositoryPatterns.Any(pattern =>
            Regex.IsMatch(url, pattern, RegexOptions.IgnoreCase)
        );
    }

    private static bool IsBugTrackingUrl(string url)
    {
        return BugTrackingPatterns.Any(pattern =>
            Regex.IsMatch(url, pattern, RegexOptions.IgnoreCase)
        );
    }

    private async Task ProcessRepositoryUrl(
        string url,
        string fullText,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        if (result.ArtifactRepositories.Any(r => r.Url == url))
            return;

        var context = ExtractUrlContext(url, fullText);
        var (isValid, confidence, reasoning) = await VerifyArtifactWithContext(
            paper,
            url,
            context,
            cancellationToken
        );

        if (isValid && confidence >= MinAcceptableConfidence)
        {
            AddVerifiedRepository(url, result, confidence, reasoning);
        }
        else
        {
            LogRejectedUrl(url, reasoning);
        }
    }

    private static string ExtractUrlContext(string url, string fullText)
    {
        var urlIndex = fullText.IndexOf(url, StringComparison.OrdinalIgnoreCase);
        var contextStart = Math.Max(0, urlIndex - ContextWindowSize);
        var contextEnd = Math.Min(fullText.Length, urlIndex + url.Length + ContextWindowSize);

        return fullText.Substring(contextStart, contextEnd - contextStart);
    }

    private void AddVerifiedRepository(
        string url,
        BugListDiscoveryResult result,
        double confidence,
        string reasoning
    )
    {
        var type = GetRepositoryType(url);
        result.ArtifactRepositories.Add(
            new ArtifactRepository
            {
                Url = url,
                Type = type,
                DiscoveryMethod = "PDF Keyword Analysis + LLM Verification",
                Confidence = Math.Min(confidence * KeywordLlmConfidenceMultiplier, 1.0),
            }
        );

        logger.LogInformation(
            "Verified artifact repository via keywords: {Url} - {Reasoning}",
            url,
            reasoning
        );
    }

    private void LogRejectedUrl(string url, string reasoning)
    {
        logger.LogDebug("Rejected keyword-found URL {Url}: {Reasoning}", url, reasoning);
    }

    private void ProcessBugTrackingUrl(string url, BugListDiscoveryResult result)
    {
        if (result.BugLists.Any(b => b.Url == url))
            return;

        var type = GetBugTrackingType(url);
        result.BugLists.Add(
            new BugListSource
            {
                Url = url,
                Type = type,
                DiscoveryMethod = "PDF Keyword Analysis",
                Confidence = KeywordAnalysisConfidence,
            }
        );

        logger.LogInformation("Found bug tracking URL via keywords: {Url}", url);
    }

    private void ProcessKeywordSentences(string normalizedText, BugListDiscoveryResult result)
    {
        var sentences = normalizedText.Split(
            new char[] { '.', '\n' },
            StringSplitOptions.RemoveEmptyEntries
        );

        foreach (var sentence in sentences)
        {
            if (ContainsArtifactKeywords(sentence))
            {
                ProcessUrlsInSentence(sentence, result);
            }
        }
    }

    private static bool ContainsArtifactKeywords(string sentence)
    {
        var lowerSentence = sentence.ToLowerInvariant();
        return ArtifactKeywords.Any(keyword => lowerSentence.Contains(keyword.ToLowerInvariant()));
    }

    private void ProcessUrlsInSentence(string sentence, BugListDiscoveryResult result)
    {
        var urlPattern = @"https?://[^\s\)]+";
        var matches = Regex.Matches(sentence, urlPattern, RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            var url = CleanUrl(match.Value);

            if (IsRepositoryUrl(url) && !result.ArtifactRepositories.Any(r => r.Url == url))
            {
                AddKeywordFoundRepository(url, result);
            }
        }
    }

    private void AddKeywordFoundRepository(string url, BugListDiscoveryResult result)
    {
        var type = GetRepositoryType(url);
        result.ArtifactRepositories.Add(
            new ArtifactRepository
            {
                Url = url,
                Type = type,
                DiscoveryMethod = "PDF Keyword Analysis",
                Confidence = KeywordAnalysisConfidence,
            }
        );

        logger.LogInformation("Found artifact repository via sentence keywords: {Url}", url);
    }

    private async Task AnalyzeTextWithLlm(
        Paper paper,
        string fullText,
        BugListDiscoveryResult result,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var truncatedText = TruncateTextForLlm(fullText);
            var analysisResponse = await GetLlmAnalysis(paper, truncatedText, cancellationToken);

            if (analysisResponse?.Mentions != null)
            {
                await ProcessLlmMentions(
                    analysisResponse.Mentions,
                    result,
                    paper,
                    cancellationToken
                );
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not analyze text with LLM for paper {Title}", paper.Title);
        }
    }

    private static string TruncateTextForLlm(string fullText)
    {
        return fullText.Substring(0, Math.Min(fullText.Length, MaxTextLengthForLlm));
    }

    private async Task<BugListAnalysisResponse?> GetLlmAnalysis(
        Paper paper,
        string text,
        CancellationToken cancellationToken
    )
    {
        var request = new BugListAnalysisRequest(paper.Title, text);
        var schema = GenerateJsonSchema<BugListAnalysisResponse>();

        var messages = CreateAnalysisMessages(request);
        var options = CreateChatOptions("bug-list-analysis", schema);

        var chatClient = kernel.GetRequiredService<ChatClient>();
        var response = await chatClient.CompleteChatAsync(messages, options, cancellationToken);

        return DeserializeLlmResponse<BugListAnalysisResponse>(
            response,
            ExportModelJsonContext.Default.BugListAnalysisResponse
        );
    }

    private async Task ProcessLlmMentions(
        List<ArtifactMention> mentions,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        foreach (var mention in mentions)
        {
            if (!string.IsNullOrWhiteSpace(mention.ProjectName))
            {
                await SearchForProject(mention, result, paper, cancellationToken);
            }
        }
    }

    private async Task SearchForArtifactsIfNeeded(
        Paper paper,
        BugListDiscoveryResult result,
        CancellationToken cancellationToken
    )
    {
        if (HasHighConfidenceArtifacts(result))
        {
            LogSkippingWebSearch(result);
            return;
        }

        logger.LogDebug(
            "No high-confidence artifacts found (total artifacts: {Count}), proceeding with web search",
            result.ArtifactRepositories.Count
        );

        await PerformWebSearch(paper, result, cancellationToken);
    }

    private bool HasHighConfidenceArtifacts(BugListDiscoveryResult result)
    {
        return result.ArtifactRepositories.Any(a => a.Confidence >= HighConfidenceThreshold);
    }

    private void LogSkippingWebSearch(BugListDiscoveryResult result)
    {
        var highConfidenceArtifacts = result
            .ArtifactRepositories.Where(a => a.Confidence >= HighConfidenceThreshold)
            .ToList();

        logger.LogInformation(
            "Found {Count} high-confidence artifacts via keyword analysis (confidence >= {Threshold}), skipping web search",
            highConfidenceArtifacts.Count,
            HighConfidenceThreshold
        );

        foreach (var artifact in highConfidenceArtifacts)
        {
            logger.LogDebug(
                "High-confidence artifact: {Url} (confidence: {Confidence})",
                artifact.Url,
                artifact.Confidence
            );
        }
    }

    private async Task PerformWebSearch(
        Paper paper,
        BugListDiscoveryResult result,
        CancellationToken cancellationToken
    )
    {
        var searchQueries = GenerateSearchQueries(paper);

        foreach (var query in searchQueries.Take(MaxSearchAttempts))
        {
            result.SearchAttempts++;
            logger.LogInformation("Searching for artifacts with query: {Query}", query);

            var searchResults = await webSearchService.SearchAsync(query, 10, cancellationToken);
            await ProcessSearchResults(searchResults, result, paper, cancellationToken);

            if (HasFoundArtifacts(result))
                break;
        }
    }

    private async Task ProcessSearchResults(
        List<WebSearchResult> searchResults,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        foreach (var searchResult in searchResults)
        {
            await ProcessSingleSearchResult(searchResult, result, paper, cancellationToken);
        }
    }

    private async Task SearchForProject(
        ArtifactMention mention,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        var query = $"{mention.ProjectName} github repository";
        var searchResults = await webSearchService.SearchAsync(query, 5, cancellationToken);

        foreach (var searchResult in searchResults)
        {
            if (IsKnownRepositoryHost(searchResult.Url))
            {
                await VerifyAndAddProjectRepository(
                    searchResult,
                    mention,
                    result,
                    paper,
                    cancellationToken
                );
            }
        }
    }

    private static bool IsKnownRepositoryHost(string url)
    {
        var hosts = new[]
        {
            "github.com",
            "gitlab.com",
            "bitbucket.org",
            "zenodo.org",
            "figshare.com",
            "osf.io",
        };

        return hosts.Any(host => url.Contains(host));
    }

    private async Task VerifyAndAddProjectRepository(
        WebSearchResult searchResult,
        ArtifactMention mention,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        var (isValid, confidence, reasoning) = await VerifyRepositoryAsync(
            paper,
            searchResult.Url,
            cancellationToken
        );

        if (isValid && confidence >= MinAcceptableConfidence)
        {
            AddProjectRepository(searchResult.Url, mention, result, confidence, reasoning);
        }
        else
        {
            LogRejectedProjectRepository(searchResult.Url, mention.ProjectName, reasoning);
        }
    }

    private void AddProjectRepository(
        string url,
        ArtifactMention mention,
        BugListDiscoveryResult result,
        double confidence,
        string reasoning
    )
    {
        var type = GetRepositoryType(url);
        var finalConfidence = Math.Min(
            mention.Confidence * confidence * LlmVerificationMultiplier,
            1.0
        );

        result.ArtifactRepositories.Add(
            new ArtifactRepository
            {
                Url = url,
                Type = type,
                DiscoveryMethod =
                    $"LLM Analysis + Web Search + Verification ({mention.ProjectName})",
                Confidence = finalConfidence,
            }
        );

        logger.LogInformation(
            "Verified project repository {Url} for {ProjectName}: {Reasoning}",
            url,
            mention.ProjectName,
            reasoning
        );
    }

    private void LogRejectedProjectRepository(string url, string projectName, string reasoning)
    {
        logger.LogDebug(
            "Project repository {Url} rejected for {ProjectName}: {Reasoning}",
            url,
            projectName,
            reasoning
        );
    }

    private List<string> GenerateSearchQueries(Paper paper)
    {
        var queries = new List<string>();
        var firstAuthor = ExtractFirstAuthorLastName(paper);
        var titleWords = ExtractSignificantTitleWords(paper);

        AddWordBasedQueries(queries, titleWords, firstAuthor);
        AddPaperBasedQueries(queries, paper, firstAuthor);

        return queries;
    }

    private static string ExtractFirstAuthorLastName(Paper paper)
    {
        return paper.Authors.FirstOrDefault()?.Split(' ').LastOrDefault() ?? "";
    }

    private static IEnumerable<string> ExtractSignificantTitleWords(Paper paper)
    {
        return paper
            .Title.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3 && char.IsUpper(w[0]))
            .Take(3);
    }

    private static void AddWordBasedQueries(
        List<string> queries,
        IEnumerable<string> titleWords,
        string firstAuthor
    )
    {
        foreach (var word in titleWords)
        {
            queries.AddRange(
                new[]
                {
                    $"{word} {firstAuthor} github repository",
                    $"{word} {firstAuthor} zenodo",
                    $"{word} {firstAuthor} figshare",
                    $"{word} source code implementation",
                    $"{word} replication package",
                }
            );
        }
    }

    private void AddPaperBasedQueries(List<string> queries, Paper paper, string firstAuthor)
    {
        var currentYear = DateTime.Now.Year;

        queries.AddRange(
            new[]
            {
                $"\"{paper.Title}\" artifact repository",
                $"\"{paper.Title}\" replication package",
                $"\"{paper.Title}\" zenodo figshare",
                $"{firstAuthor} {currentYear} software repository",
                $"{firstAuthor} {currentYear} artifact doi",
            }
        );
    }

    private async Task ProcessSingleSearchResult(
        WebSearchResult searchResult,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        if (IsRepositoryUrl(searchResult.Url))
        {
            await ProcessRepositorySearchResult(searchResult, result, paper, cancellationToken);
        }
        else if (IsBugTrackingUrl(searchResult.Url))
        {
            ProcessBugTrackingSearchResult(searchResult, result);
        }
    }

    private async Task ProcessRepositorySearchResult(
        WebSearchResult searchResult,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        var (isValid, confidence, reasoning) = await VerifyRepositoryAsync(
            paper,
            searchResult.Url,
            cancellationToken
        );

        if (isValid && confidence >= MinAcceptableConfidence)
        {
            AddWebSearchRepository(searchResult.Url, result, confidence, reasoning, paper);
        }
        else
        {
            LogRejectedWebSearchRepository(searchResult.Url, paper.Title, reasoning);
        }
    }

    private void AddWebSearchRepository(
        string url,
        BugListDiscoveryResult result,
        double confidence,
        string reasoning,
        Paper paper
    )
    {
        var type = GetRepositoryType(url);
        result.ArtifactRepositories.Add(
            new ArtifactRepository
            {
                Url = url,
                Type = type,
                DiscoveryMethod = "Web Search + LLM Verification",
                Confidence = confidence * WebSearchConfidenceMultiplier,
            }
        );

        logger.LogInformation(
            "Verified repository {Url} for paper {Title}: {Reasoning}",
            url,
            paper.Title,
            reasoning
        );
    }

    private void LogRejectedWebSearchRepository(string url, string paperTitle, string reasoning)
    {
        logger.LogDebug(
            "Repository {Url} rejected for paper {Title}: {Reasoning}",
            url,
            paperTitle,
            reasoning
        );
    }

    private void ProcessBugTrackingSearchResult(
        WebSearchResult searchResult,
        BugListDiscoveryResult result
    )
    {
        var type = GetBugTrackingType(searchResult.Url);
        result.BugLists.Add(
            new BugListSource
            {
                Url = searchResult.Url,
                Type = type,
                DiscoveryMethod = "Web Search",
                Confidence = WebSearchConfidenceMultiplier,
            }
        );
    }

    private string GetBugTrackingType(string url)
    {
        return url switch
        {
            var u when u.Contains("github.com") => "GitHub Issues",
            var u when u.Contains("jira") => "Jira",
            var u when u.Contains("bugzilla") => "Bugzilla",
            var u when u.Contains("launchpad") => "Launchpad",
            var u when u.Contains("sourceforge") => "SourceForge",
            _ => "Unknown",
        };
    }

    private string GetRepositoryType(string url)
    {
        return url switch
        {
            var u when u.Contains("github.com") => "GitHub",
            var u when u.Contains("gitlab.com") => "GitLab",
            var u when u.Contains("bitbucket.org") => "Bitbucket",
            var u when u.Contains("sourceforge.net") => "SourceForge",
            var u when u.Contains("zenodo.org") => "Zenodo",
            var u when u.Contains("figshare.com") => "Figshare",
            var u when u.Contains("osf.io") => "OSF",
            var u when u.Contains("ieee-dataport.org") => "IEEE DataPort",
            var u when u.Contains("researchgate.net") => "ResearchGate",
            var u when u.Contains("archive.org") => "Internet Archive",
            var u when u.Contains("doi.org") => "DOI",
            _ => "Unknown",
        };
    }

    private BugListDiscoveryAnalysis CreateAnalysisFromResults(List<BugListDiscoveryResult> results)
    {
        var totalSearchAttempts = results.Sum(r => r.SearchAttempts);
        var (bugTrackingStats, repositoryStats) = CalculateStatistics(results);

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

    private static (
        Dictionary<string, int> bugTracking,
        Dictionary<string, int> repository
    ) CalculateStatistics(List<BugListDiscoveryResult> results)
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

        return (bugTrackingStats, repositoryStats);
    }

    private async Task<(bool isValid, double confidence, string reasoning)> VerifyRepositoryAsync(
        Paper paper,
        string repositoryUrl,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (!IsGitHubRepository(repositoryUrl))
            {
                return (true, 0.5, "Non-GitHub repository - cannot verify");
            }

            var (owner, repoName) = ExtractGitHubInfo(repositoryUrl);
            var repoInfo = await gitHubService.GetRepositoryInfoAsync(owner, repoName);
            var readmeContent = await GetRepositoryReadme(owner, repoName);

            return await PerformRepositoryVerification(
                paper,
                repoInfo,
                readmeContent,
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error verifying repository {Url}", repositoryUrl);
            return (false, 0.1, $"Verification error: {ex.Message}");
        }
    }

    private static bool IsGitHubRepository(string url) => GithubComPattern().IsMatch(url);

    private static (string owner, string repoName) ExtractGitHubInfo(string repositoryUrl)
    {
        var match = GithubComPattern().Match(repositoryUrl);
        return (match.Groups[1].Value, match.Groups[2].Value);
    }

    private async Task<string> GetRepositoryReadme(string owner, string repoName)
    {
        var readmeContent = await gitHubService.GetRepositoryReadmeAsync(owner, repoName);

        if (string.IsNullOrEmpty(readmeContent))
        {
            logger.LogDebug(
                "No README found for {Owner}/{RepoName}, using basic verification",
                owner,
                repoName
            );
            return "No README available";
        }

        return readmeContent.Length > MaxReadmeLength
            ? string.Concat(readmeContent.AsSpan(0, MaxReadmeLength), "...")
            : readmeContent;
    }

    private async Task<(
        bool isValid,
        double confidence,
        string reasoning
    )> PerformRepositoryVerification(
        Paper paper,
        dynamic repoInfo,
        string readmeContent,
        CancellationToken cancellationToken
    )
    {
        var request = new RepositoryVerificationRequest(
            paper.Title,
            string.Join(", ", paper.Authors),
            repoInfo.FullName ?? $"{repoInfo.Owner}/{repoInfo.Name}",
            repoInfo.Description ?? "",
            readmeContent
        );

        var response = await GetVerificationResponse<RepositoryVerificationResponse>(
            request,
            "repository-verification",
            CreateRepositoryVerificationMessages,
            ExportModelJsonContext.Default.RepositoryVerificationResponse,
            cancellationToken
        );

        if (response != null)
        {
            LogVerificationResult(
                request.RepositoryName,
                response.IsArtifactRepository,
                response.Confidence,
                response.Reasoning
            );
            return (response.IsArtifactRepository, response.Confidence, response.Reasoning);
        }

        return (false, 0.1, "Failed to parse verification response");
    }

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
            var request = new ArtifactVerificationRequest(
                paper.Title,
                string.Join(", ", paper.Authors),
                url,
                context
            );

            var response = await GetVerificationResponse<ArtifactVerificationResponse>(
                request,
                "artifact-verification",
                CreateArtifactVerificationMessages,
                ExportModelJsonContext.Default.ArtifactVerificationResponse,
                cancellationToken
            );

            if (response != null)
            {
                LogArtifactVerificationResult(
                    url,
                    response.IsArtifact,
                    response.Confidence,
                    response.Reasoning
                );
                return (response.IsArtifact, response.Confidence, response.Reasoning);
            }

            return (false, 0.1, "Failed to parse verification response");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error verifying artifact {Url}", url);
            return (false, 0.1, $"Verification error: {ex.Message}");
        }
    }

    private async Task<T?> GetVerificationResponse<T>(
        object request,
        string schemaName,
        Func<object, ChatMessage[]> messageFactory,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken
    )
        where T : class
    {
        var schema = GenerateJsonSchema<T>();
        var messages = messageFactory(request);
        var options = CreateChatOptions(schemaName, schema);

        var smallModelClient = kernel
            .GetRequiredService<OpenAIClient>()
            .GetChatClient(credentialOptions.Value.SmallModel);

        var response = await smallModelClient.CompleteChatAsync(
            messages,
            options,
            cancellationToken
        );
        return DeserializeLlmResponse<T>(response, jsonTypeInfo);
    }

    private void LogVerificationResult(
        string repoName,
        bool isValid,
        double confidence,
        string reasoning
    ) =>
        logger.LogDebug(
            "Repository verification for {Repo}: {IsValid} (confidence: {Confidence}) - {Reasoning}",
            repoName,
            isValid,
            confidence,
            reasoning
        );

    private void LogArtifactVerificationResult(
        string url,
        bool isValid,
        double confidence,
        string reasoning
    ) =>
        logger.LogDebug(
            "Artifact verification for {Url}: {IsValid} (confidence: {Confidence}) - {Reasoning}",
            url,
            isValid,
            confidence,
            reasoning
        );

    private static string GenerateJsonSchema<T>() =>
        new DefaultSchemaGenerator().Generate<T>(new JsonSchemaOptions()).ToJson();

    private static ChatMessage[] CreateAnalysisMessages(BugListAnalysisRequest request) =>
        [
            new SystemChatMessage(
                "Analyze research paper text and identify any mentions of bug tracking systems, issue trackers, software repositories, or project websites. Look for indirect references like 'Our code is available at...', 'Issues can be reported at...', 'The implementation can be found...', repository names without full URLs, or project names that might have public repositories."
            ),
            new UserChatMessage($"Paper Title: {request.PaperTitle}\n\nText: {request.PaperText}"),
        ];

    private static ChatMessage[] CreateRepositoryVerificationMessages(object request)
    {
        var req = (RepositoryVerificationRequest)request;
        return
        [
            new SystemChatMessage(
                "You are tasked with determining if a GitHub repository is an artifact repository for a specific research paper. "
                    + "An artifact repository should contain the actual implementation, data, or tools described in the paper, "
                    + "not just a collection of papers or general-purpose tools. "
                    + "Look for evidence that this repository specifically implements or supports the research described in the paper. "
                    + "Consider repository name, description, README content, and whether it matches the paper's focus."
            ),
            new UserChatMessage(
                $"Paper Title: {req.PaperTitle}\n"
                    + $"Authors: {req.PaperAuthors}\n"
                    + $"Repository: {req.RepositoryName}\n"
                    + $"Description: {req.RepositoryDescription}\n"
                    + $"README Content: {req.ReadmeContent}"
            ),
        ];
    }

    private static ChatMessage[] CreateArtifactVerificationMessages(object request)
    {
        var req = (ArtifactVerificationRequest)request;
        return
        [
            new SystemChatMessage(
                "You are tasked with determining if a URL is an artifact for a specific research paper. "
                    + "An artifact should contain the actual implementation, data, or tools described in the paper, "
                    + "not just a collection of papers or general-purpose tools. "
                    + "Look for evidence that this URL specifically implements or supports the research described in the paper. "
                    + "Consider the URL itself, the context around the URL, and whether it matches the paper's focus."
            ),
            new UserChatMessage(
                $"Paper Title: {req.PaperTitle}\n"
                    + $"Authors: {req.PaperAuthors}\n"
                    + $"URL: {req.Url}\n"
                    + $"Context: {req.Context}"
            ),
        ];
    }

    private static ChatCompletionOptions CreateChatOptions(string schemaName, string schema) =>
        new()
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: schemaName,
                jsonSchema: BinaryData.FromString(schema),
                jsonSchemaIsStrict: true
            ),
        };

    private static T? DeserializeLlmResponse<T>(
        ClientResult<ChatCompletion> response,
        JsonTypeInfo<T> jsonTypeInfo
    )
        where T : class
    {
        if (response.Value.Content.Count == 0)
            return null;

        var resultText = response.Value.Content[0].Text;
        if (string.IsNullOrEmpty(resultText))
            return null;

        return JsonSerializer.Deserialize(resultText, jsonTypeInfo);
    }

    [GeneratedRegex(@"github\.com/([^/]+)/([^/]+)/?$")]
    private static partial Regex GithubComPattern();
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
