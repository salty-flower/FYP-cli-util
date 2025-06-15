using System.Text.RegularExpressions;
using DataCollection.Application.Common.Services;
using DataCollection.Application.Features.BugDiscovery;
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
public partial class PdfContentAnalysisService(
    DatabaseDataLoadingService databaseDataLoadingService,
    IPatternMatchingService patternMatchingService,
    ILogger<PdfContentAnalysisService> logger,
    OpenAIClient oaiClient,
    IOptionsSnapshot<LLMOptions> llmOpts,
    LlmPromptService llmPromptService,
    ValidationService validationService
)
{
    private readonly ChatClient chatClient = oaiClient.GetChatClient(
        llmOpts.Value.PdfContentAnalysisModel
    );

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
    public async Task<PdfAnalysisResult> ExtractFromPdfContent(
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        validationService.ValidatePaper(paper);

        var pdfData = await LoadPdfDataAsync(paper);
        if (pdfData == null)
        {
            logger.LogWarning("No PDF data found for paper {PaperDoi}", paper.Doi);
            return new PdfAnalysisResult();
        }

        var fullText = string.Join(" ", pdfData.TextLines).RemoveLineEndings();
        if (string.IsNullOrWhiteSpace(fullText))
        {
            logger.LogWarning("Empty PDF content for paper {PaperDoi}", paper.Doi);
            return new PdfAnalysisResult();
        }

        LogPdfAnalysis(paper.Doi, "started", fullText.Length);

        var bugLists = new List<BugListSource>();
        var artifactRepositories = new List<ArtifactRepository>();

        // Extract direct URLs first
        await ExtractDirectUrls(fullText, bugLists, artifactRepositories, cancellationToken);

        // Extract structured bug lists from tables
        await ExtractStructuredBugLists(pdfData, bugLists, cancellationToken);

        // Extract artifacts by keywords
        await ExtractArtifactsByKeywords(fullText, artifactRepositories, paper, cancellationToken);

        // Process URLs found in text
        await ProcessUrlsInText(fullText, bugLists, artifactRepositories, paper, cancellationToken);

        // Analyze with LLM for additional insights
        await AnalyzeTextWithLlm(
            paper,
            fullText,
            bugLists,
            artifactRepositories,
            cancellationToken
        );

        return new PdfAnalysisResult
        {
            BugLists = bugLists.DistinctBy(b => b.Url).ToList(),
            ArtifactRepositories = artifactRepositories.DistinctBy(r => r.Url).ToList(),
        };
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
        List<BugListSource> bugLists,
        List<ArtifactRepository> artifactRepositories,
        CancellationToken cancellationToken
    )
    {
        await ExtractBugTrackingUrls(text.Replace(" ", ""), bugLists, cancellationToken);
        await ExtractRepositoryUrls(text.Replace(" ", ""), artifactRepositories, cancellationToken);
    }

    private async Task ExtractBugTrackingUrls(
        string text,
        List<BugListSource> bugLists,
        CancellationToken cancellationToken
    )
    {
        var matches = await patternMatchingService.FindBugTrackingUrlsAsync(
            text,
            cancellationToken
        );

        foreach (var match in matches)
        {
            if (!bugLists.Any(b => b.Url.Equals(match.Value, StringComparison.OrdinalIgnoreCase)))
            {
                var urlType = await patternMatchingService.DetermineUrlTypeAsync(
                    match.Value,
                    cancellationToken
                );
                var confidence = patternMatchingService.CalculateConfidence(text, "PdfAnalysis", 1);

                bugLists.Add(
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
        List<ArtifactRepository> artifactRepositories,
        CancellationToken cancellationToken
    )
    {
        var matches = await patternMatchingService.FindRepositoryUrlsAsync(text, cancellationToken);

        foreach (var match in matches)
        {
            if (
                !artifactRepositories.Any(r =>
                    r.Url.Equals(match.Value, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                var urlType = await patternMatchingService.DetermineUrlTypeAsync(
                    match.Value,
                    cancellationToken
                );
                var confidence = patternMatchingService.CalculateConfidence(text, "PdfAnalysis", 1);

                artifactRepositories.Add(
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
        List<BugListSource> bugLists,
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

                bugLists.Add(
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
        var start = Math.Max(0, matchIndex - BugDiscoveryConstants.ContextWindowSize);
        var end = Math.Min(text.Length, matchIndex + BugDiscoveryConstants.ContextWindowSize);
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

        var start = Math.Max(0, index - BugDiscoveryConstants.ContextWindowSize * 2);
        var end = Math.Min(fullText.Length, index + BugDiscoveryConstants.ContextWindowSize * 2);
        return fullText.Substring(start, end - start);
    }

    private double CalculateBugListConfidence(PdfBugTableInfo tableInfo, int issueCount)
    {
        var title = tableInfo.Title.ToLowerInvariant();
        var hasKeywords =
            title.Contains("bug") || title.Contains("issue") || title.Contains("defect");
        return ConfidenceCalculationService.CalculatePdfAnalysisConfidence(issueCount, hasKeywords);
    }

    private async Task ExtractArtifactsByKeywords(
        string text,
        List<ArtifactRepository> artifactRepositories,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        var normalizedText = text.ToLowerInvariant();

        // Process artifact sections
        await ProcessArtifactSections(
            normalizedText,
            artifactRepositories,
            paper,
            cancellationToken
        );

        // Process keyword sentences
        await ProcessKeywordSentences(normalizedText, artifactRepositories, cancellationToken);
    }

    private async Task ProcessArtifactSections(
        string normalizedText,
        List<ArtifactRepository> artifactRepositories,
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
            await ProcessUrlsInText(
                sectionContent,
                new List<BugListSource>(),
                artifactRepositories,
                paper,
                cancellationToken
            ); // We only care about repos here
        }
    }

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    private async Task ProcessUrlsInText(
        string text,
        List<BugListSource> bugLists,
        List<ArtifactRepository> artifactRepositories,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        var matches = UrlRegex().Matches(text);

        foreach (Match match in matches)
        {
            var url = match.Value.TrimEnd('.', ',', ';', ')', ']', '}', ' ', '\t', '\n', '\r');
            await ProcessFoundUrl(
                url,
                text,
                bugLists,
                artifactRepositories,
                paper,
                cancellationToken
            );
        }
    }

    private async Task ProcessFoundUrl(
        string url,
        string fullText,
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

        if (repoMatches.Any())
        {
            await ProcessRepositoryUrl(
                url,
                fullText,
                artifactRepositories,
                paper,
                cancellationToken
            );
        }
        else if (bugMatches.Any())
        {
            if (!bugLists.Any(b => b.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
            {
                var urlType = await patternMatchingService.DetermineUrlTypeAsync(
                    url,
                    cancellationToken
                );
                var confidence = patternMatchingService.CalculateConfidence(
                    fullText,
                    "PdfAnalysis",
                    1
                );

                bugLists.Add(
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
        List<ArtifactRepository> artifactRepositories,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        if (!artifactRepositories.Any(r => r.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
        {
            var context = ExtractUrlContext(url, fullText);
            var hasKeywords = await ContainsArtifactKeywords(context, cancellationToken);
            var confidence = ConfidenceCalculationService.CalculateKeywordAnalysisConfidence(
                context,
                hasKeywords
            );

            artifactRepositories.Add(
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

        var start = Math.Max(0, index - BugDiscoveryConstants.ContextWindowSize);
        var end = Math.Min(
            fullText.Length,
            index + url.Length + BugDiscoveryConstants.ContextWindowSize
        );
        return fullText.Substring(start, end - start);
    }

    private async Task ProcessKeywordSentences(
        string normalizedText,
        List<ArtifactRepository> artifactRepositories,
        CancellationToken cancellationToken
    )
    {
        var sentences = normalizedText.Split('.', '!', '?');

        foreach (var sentence in sentences)
        {
            if (await ContainsArtifactKeywords(sentence, cancellationToken))
            {
                await ProcessUrlsInSentence(sentence, artifactRepositories, cancellationToken);
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
        List<ArtifactRepository> artifactRepositories,
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
                !artifactRepositories.Any(r =>
                    r.Url.Equals(match.Value, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                var urlType = await patternMatchingService.DetermineUrlTypeAsync(
                    match.Value,
                    cancellationToken
                );
                var confidence = ConfidenceCalculationService.CalculateKeywordAnalysisConfidence(
                    sentence,
                    true
                );

                artifactRepositories.Add(
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

    private void LogPdfAnalysis(string paperDoi, string action, int? textLength = null)
    {
        if (textLength.HasValue)
            logger.LogInformation(
                "PDF analysis {Action} for paper {PaperDoi} ({TextLength} chars)",
                action,
                paperDoi,
                textLength.Value
            );
        else
            logger.LogInformation("PDF analysis {Action} for paper {PaperDoi}", action, paperDoi);
    }

    private async Task AnalyzeTextWithLlm(
        Paper paper,
        string fullText,
        List<BugListSource> bugLists,
        List<ArtifactRepository> artifactRepositories,
        CancellationToken cancellationToken
    )
    {
        var truncatedText = TruncateTextForLlm(fullText);
        var analysis = await GetLlmAnalysis(paper, truncatedText, cancellationToken);

        if (analysis != null)
        {
            // TODO: Process LLM analysis results
            // This would parse the LLM response and add findings to the local lists
            LogPdfAnalysis(paper.Doi, "LLM analysis completed");
        }
    }

    private string TruncateTextForLlm(string fullText)
    {
        return fullText.Length > BugDiscoveryConstants.MaxTextLengthForLlm
            ? fullText.Substring(0, BugDiscoveryConstants.MaxTextLengthForLlm)
            : fullText;
    }

    private async Task<string?> GetLlmAnalysis(
        Paper paper,
        string text,
        CancellationToken cancellationToken
    )
    {
        var prompt = llmPromptService.CreatePdfAnalysisPrompt(paper, text);
        var messages = new[] { new UserChatMessage(prompt) };
        var response = await chatClient.CompleteChatAsync(
            messages,
            cancellationToken: cancellationToken
        );
        return response?.Value?.Content?.FirstOrDefault()?.Text;
    }
}
