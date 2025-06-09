using System.Text.RegularExpressions;
using DataCollection.Application.Features.PatternMatching;
using DataCollection.Common.Extensions;
using DataCollection.Core.Models;
using DataCollection.Infrastructure.Models.BugList;
using DataCollection.Infrastructure.Options;
using DataCollection.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace DataCollection.Application.Features.PaperAnalysis;

/// <summary>
/// Helper class for PDF table analysis
/// </summary>
internal class PdfBugTableInfo
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public int Position { get; set; }
}

/// <summary>
/// Service responsible for analyzing PDF content to extract bug lists and artifact repositories
/// </summary>
public class PdfContentAnalysisService(
    DatabaseDataLoadingService databaseDataLoadingService,
    IPatternMatchingService patternMatchingService,
    ILogger<PdfContentAnalysisService> logger,
    OpenAIClient oaiClient,
    IOptionsSnapshot<LLMOptions> llmOpts,
    IOptions<PathsOptions> pathsOptions
)
{
    private readonly ChatClient chatClient = oaiClient.GetChatClient(
        llmOpts.Value.PdfContentAnalysisModel
    );

    // Constants
    private const int ContextWindowSize = 200;
    private const int MaxTextLengthForLlm = 4000;

    // Issue number patterns - keeping these as they're specific to PDF parsing
    private static readonly string[] IssueNumberPatterns =
    [
        @"#\d+",
        @"issue\s*#?\s*\d+",
        @"bug\s*#?\s*\d+",
        @"ticket\s*#?\s*\d+",
        @"defect\s*#?\s*\d+",
        @"fault\s*#?\s*\d+",
        @"problem\s*#?\s*\d+",
        @"error\s*#?\s*\d+",
        @"failure\s*#?\s*\d+",
        @"exception\s*#?\s*\d+",
    ];

    /// <summary>
    /// Main method to extract bug lists and artifacts from PDF content
    /// </summary>
    public async Task ExtractFromPdfContent(
        Paper paper,
        BugListDiscoveryResult result,
        CancellationToken cancellationToken
    )
    {
        var pdfData = await LoadPdfDataAsync(paper);
        if (pdfData == null)
        {
            logger.LogWarning("No PDF data found for paper {PaperDoi}", paper.Doi);
            return;
        }

        var fullText = string.Join(" ", pdfData.TextLines).RemoveLineEndings();
        if (string.IsNullOrWhiteSpace(fullText))
        {
            logger.LogWarning("Empty PDF content for paper {PaperDoi}", paper.Doi);
            return;
        }

        logger.LogInformation(
            "Analyzing PDF content for paper {PaperDoi} ({TextLength} chars)",
            paper.Doi,
            fullText.Length
        );

        // Extract direct URLs first
        await ExtractDirectUrls(fullText, result, cancellationToken);

        // Extract structured bug lists from tables
        await ExtractStructuredBugLists(pdfData, result, cancellationToken);

        // Extract artifacts by keywords
        await ExtractArtifactsByKeywords(fullText, result, paper, cancellationToken);

        // Process URLs found in text
        await ProcessUrlsInText(fullText, result, paper, cancellationToken);

        // Analyze with LLM for additional insights
        await AnalyzeTextWithLlm(paper, fullText, result, cancellationToken);
    }

    private async Task<PdfData?> LoadPdfDataAsync(Paper paper)
    {
        try
        {
            return await databaseDataLoadingService.LoadPdfDataAsync(paper.Doi);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load PDF data for paper {PaperDoi}", paper.Doi);
            return null;
        }
    }

    private async Task ExtractDirectUrls(
        string text,
        BugListDiscoveryResult result,
        CancellationToken cancellationToken
    )
    {
        await ExtractBugTrackingUrls(text.Replace(" ", ""), result, cancellationToken);
        await ExtractRepositoryUrls(text.Replace(" ", ""), result, cancellationToken);
    }

    private async Task ExtractBugTrackingUrls(
        string text,
        BugListDiscoveryResult result,
        CancellationToken cancellationToken
    )
    {
        var matches = await patternMatchingService.FindBugTrackingUrlsAsync(
            text,
            cancellationToken
        );

        foreach (var match in matches)
        {
            if (
                !result.BugLists.Any(b =>
                    b.Url.Equals(match.Value, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                var urlType = await patternMatchingService.DetermineUrlTypeAsync(
                    match.Value,
                    cancellationToken
                );
                var confidence = await patternMatchingService.CalculateConfidenceAsync(
                    text,
                    "PdfAnalysis",
                    1,
                    cancellationToken
                );

                result.BugLists.Add(
                    new BugListSource
                    {
                        Url = match.Value,
                        Type = urlType,
                        DiscoveryMethod = "PDF Direct Extraction",
                        Confidence = confidence,
                        TableContext = match.Context,
                    }
                );
            }
        }
    }

    private async Task ExtractRepositoryUrls(
        string text,
        BugListDiscoveryResult result,
        CancellationToken cancellationToken
    )
    {
        var matches = await patternMatchingService.FindRepositoryUrlsAsync(text, cancellationToken);

        foreach (var match in matches)
        {
            if (
                !result.ArtifactRepositories.Any(r =>
                    r.Url.Equals(match.Value, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                var urlType = await patternMatchingService.DetermineUrlTypeAsync(
                    match.Value,
                    cancellationToken
                );
                var confidence = await patternMatchingService.CalculateConfidenceAsync(
                    text,
                    "PdfAnalysis",
                    1,
                    cancellationToken
                );

                result.ArtifactRepositories.Add(
                    new ArtifactRepository
                    {
                        Url = match.Value,
                        Type = urlType,
                        DiscoveryMethod = "PDF Direct Extraction",
                        Confidence = confidence,
                    }
                );
            }
        }
    }

    private async Task ExtractStructuredBugLists(
        PdfData pdfData,
        BugListDiscoveryResult result,
        CancellationToken cancellationToken
    )
    {
        var fullText = string.Join(" ", pdfData.TextLines);
        var bugTables = await FindBugTables(fullText, cancellationToken);

        foreach (var tableInfo in bugTables)
        {
            var issueNumbers = ExtractIssueNumbers(tableInfo.Content);
            var validIssues = issueNumbers.Where(IsValidIssueNumber).ToList();

            if (validIssues.Count > 0)
            {
                var confidence = CalculateBugListConfidence(tableInfo, validIssues.Count);

                // Try to infer repository from context
                var repository = await InferRepositoryFromContext(
                    tableInfo,
                    fullText,
                    cancellationToken
                );

                result.BugLists.Add(
                    new BugListSource
                    {
                        Url = repository ?? "Unknown",
                        Type =
                            repository != null
                                ? await patternMatchingService.DetermineUrlTypeAsync(
                                    repository,
                                    cancellationToken
                                )
                                : "Unknown",
                        DiscoveryMethod = "PDF Table Analysis",
                        Confidence = confidence,
                        IssueNumbers = validIssues,
                        TableContext =
                            $"Table: {tableInfo.Title}, Issues: {string.Join(", ", validIssues.Take(5))}"
                            + (validIssues.Count > 5 ? $" and {validIssues.Count - 5} more" : ""),
                    }
                );
            }
        }
    }

    private async Task<List<PdfBugTableInfo>> FindBugTables(
        string text,
        CancellationToken cancellationToken
    )
    {
        var matches = await patternMatchingService.FindKeywordMatchesAsync(
            text,
            "BugList",
            cancellationToken
        );
        var tables = new List<PdfBugTableInfo>();

        foreach (var match in matches)
        {
            var context = ExtractTableContext(text, match.Position);
            tables.Add(
                new PdfBugTableInfo
                {
                    Title = match.Value,
                    Content = context,
                    Position = match.Position,
                }
            );
        }

        return tables.GroupBy(t => t.Position / 1000).Select(g => g.First()).ToList();
    }

    private string ExtractTableContext(string text, int matchIndex)
    {
        var start = Math.Max(0, matchIndex - ContextWindowSize);
        var end = Math.Min(text.Length, matchIndex + ContextWindowSize);
        return text.Substring(start, end - start);
    }

    private List<string> ExtractIssueNumbers(string tableContent)
    {
        var issueNumbers = new List<string>();

        foreach (var pattern in IssueNumberPatterns)
        {
            var matches = Regex.Matches(tableContent, pattern, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                var issueNumber = Regex.Replace(match.Value, @"[^\d]", "");
                if (!string.IsNullOrEmpty(issueNumber))
                {
                    issueNumbers.Add(issueNumber);
                }
            }
        }

        return issueNumbers.Distinct().ToList();
    }

    private bool IsValidIssueNumber(string issueNumber)
    {
        if (string.IsNullOrEmpty(issueNumber))
            return false;

        if (!int.TryParse(issueNumber, out var number))
            return false;

        // Basic validation: issue numbers should be reasonable
        return number > 0 && number < 1000000; // Reasonable upper limit
    }

    private async Task<string?> InferRepositoryFromContext(
        PdfBugTableInfo tableInfo,
        string fullText,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var context = ExtractSurroundingContext(fullText, tableInfo.Title);

            // Look for repository URLs near the table
            var repoMatches = await patternMatchingService.FindRepositoryUrlsAsync(
                context,
                cancellationToken
            );
            if (repoMatches.Any())
            {
                return repoMatches.First().Value;
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to infer repository from context");
            return null;
        }
    }

    private string ExtractSurroundingContext(string fullText, string tableTitle)
    {
        var index = fullText.IndexOf(tableTitle, StringComparison.OrdinalIgnoreCase);
        if (index == -1)
            return tableTitle;

        var start = Math.Max(0, index - ContextWindowSize * 2);
        var end = Math.Min(fullText.Length, index + ContextWindowSize * 2);
        return fullText.Substring(start, end - start);
    }

    private double CalculateBugListConfidence(PdfBugTableInfo tableInfo, int issueCount)
    {
        var confidence = 0.9; // PDF analysis base confidence

        // Boost confidence based on issue count
        if (issueCount > 10)
            confidence += 0.05;
        if (issueCount > 50)
            confidence += 0.05;

        // Boost confidence if table title contains specific keywords
        var title = tableInfo.Title.ToLowerInvariant();
        if (title.Contains("bug") || title.Contains("issue") || title.Contains("defect"))
            confidence += 0.05;

        return Math.Min(1.0, confidence);
    }

    private async Task ExtractArtifactsByKeywords(
        string text,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        var normalizedText = text.ToLowerInvariant();

        // Process artifact sections
        await ProcessArtifactSections(normalizedText, result, paper, cancellationToken);

        // Process keyword sentences
        await ProcessKeywordSentences(normalizedText, result, cancellationToken);
    }

    private async Task ProcessArtifactSections(
        string normalizedText,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        var artifactMatches = await patternMatchingService.FindKeywordMatchesAsync(
            normalizedText,
            "Artifact",
            cancellationToken
        );

        foreach (var match in artifactMatches)
        {
            var sectionContent = ExtractSurroundingContext(normalizedText, match.Value);
            await ProcessUrlsInText(sectionContent, result, paper, cancellationToken);
        }
    }

    private async Task ProcessUrlsInText(
        string text,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        var urlPattern = @"https?://[^\s<>""']+";
        var matches = Regex.Matches(text, urlPattern, RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            var url = match.Value.TrimEnd('.', ',', ';', ')', ']', '}', ' ', '\t', '\n', '\r');
            await ProcessFoundUrl(url, text, result, paper, cancellationToken);
        }
    }

    private async Task ProcessFoundUrl(
        string url,
        string fullText,
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
            await ProcessRepositoryUrl(url, fullText, result, paper, cancellationToken);
        }
        else if (bugMatches.Any())
        {
            if (!result.BugLists.Any(b => b.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
            {
                var urlType = await patternMatchingService.DetermineUrlTypeAsync(
                    url,
                    cancellationToken
                );
                var confidence = await patternMatchingService.CalculateConfidenceAsync(
                    fullText,
                    "PdfAnalysis",
                    1,
                    cancellationToken
                );

                result.BugLists.Add(
                    new BugListSource
                    {
                        Url = url,
                        Type = urlType,
                        DiscoveryMethod = "PDF URL Analysis",
                        Confidence = confidence,
                        TableContext = ExtractUrlContext(url, fullText),
                    }
                );
            }
        }
    }

    private async Task ProcessRepositoryUrl(
        string url,
        string fullText,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        if (
            !result.ArtifactRepositories.Any(r =>
                r.Url.Equals(url, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            var context = ExtractUrlContext(url, fullText);
            var hasKeywords = await ContainsArtifactKeywords(context, cancellationToken);
            var confidence = await patternMatchingService.CalculateConfidenceAsync(
                context,
                hasKeywords ? "KeywordAnalysis" : "PdfAnalysis",
                1,
                cancellationToken
            );

            result.ArtifactRepositories.Add(
                new ArtifactRepository
                {
                    Url = url,
                    Type = await patternMatchingService.DetermineUrlTypeAsync(
                        url,
                        cancellationToken
                    ),
                    DiscoveryMethod = "PDF URL Analysis",
                    Confidence = confidence,
                }
            );
        }
    }

    private string ExtractUrlContext(string url, string fullText)
    {
        var index = fullText.IndexOf(url, StringComparison.OrdinalIgnoreCase);
        if (index == -1)
            return "";

        var start = Math.Max(0, index - ContextWindowSize);
        var end = Math.Min(fullText.Length, index + url.Length + ContextWindowSize);
        return fullText.Substring(start, end - start);
    }

    private async Task ProcessKeywordSentences(
        string normalizedText,
        BugListDiscoveryResult result,
        CancellationToken cancellationToken
    )
    {
        var sentences = normalizedText.Split('.', '!', '?');

        foreach (var sentence in sentences)
        {
            if (await ContainsArtifactKeywords(sentence, cancellationToken))
            {
                await ProcessUrlsInSentence(sentence, result, cancellationToken);
            }
        }
    }

    private async Task<bool> ContainsArtifactKeywords(
        string sentence,
        CancellationToken cancellationToken
    )
    {
        var matches = await patternMatchingService.FindKeywordMatchesAsync(
            sentence,
            "Artifact",
            cancellationToken
        );
        return matches.Any();
    }

    private async Task ProcessUrlsInSentence(
        string sentence,
        BugListDiscoveryResult result,
        CancellationToken cancellationToken
    )
    {
        var matches = await patternMatchingService.FindRepositoryUrlsAsync(
            sentence,
            cancellationToken
        );

        foreach (var match in matches)
        {
            if (
                !result.ArtifactRepositories.Any(r =>
                    r.Url.Equals(match.Value, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                var urlType = await patternMatchingService.DetermineUrlTypeAsync(
                    match.Value,
                    cancellationToken
                );
                var confidence = await patternMatchingService.CalculateConfidenceAsync(
                    sentence,
                    "KeywordAnalysis",
                    1,
                    cancellationToken
                );

                result.ArtifactRepositories.Add(
                    new ArtifactRepository
                    {
                        Url = match.Value,
                        Type = urlType,
                        DiscoveryMethod = "PDF Keyword Analysis",
                        Confidence = confidence,
                    }
                );
            }
        }
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
            var analysis = await GetLlmAnalysis(paper, truncatedText, cancellationToken);

            if (analysis != null)
            {
                // Process LLM analysis results
                // This would parse the LLM response and add findings to result
                logger.LogDebug("LLM analysis completed for paper {PaperDoi}", paper.Doi);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LLM analysis failed for paper {PaperDoi}", paper.Doi);
        }
    }

    private string TruncateTextForLlm(string fullText)
    {
        return fullText.Length > MaxTextLengthForLlm
            ? fullText.Substring(0, MaxTextLengthForLlm)
            : fullText;
    }

    private async Task<string?> GetLlmAnalysis(
        Paper paper,
        string text,
        CancellationToken cancellationToken
    )
    {
        var prompt =
            $@"Analyze this research paper text and identify:
1. Bug tracking systems or issue trackers mentioned
2. Source code repositories or artifacts
3. Any references to bug lists, defect databases, or issue collections

Paper title: {paper.Title}
Text: {text}

Provide URLs and brief descriptions for any findings.";

        try
        {
            var messages = new[] { new UserChatMessage(prompt) };
            var response = await chatClient.CompleteChatAsync(
                messages,
                cancellationToken: cancellationToken
            );
            return response?.Value?.Content?.FirstOrDefault()?.Text;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get LLM analysis");
            return null;
        }
    }
}
