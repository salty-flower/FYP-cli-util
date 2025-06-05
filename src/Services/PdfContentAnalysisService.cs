using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DataCollection.Models;
using DataCollection.Models.Export.BugAnalysis;
using DataCollection.Options;
using DataCollection.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace DataCollection.Services;

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
    private const double PdfAnalysisConfidence = 0.9;
    private const double KeywordAnalysisConfidence = 0.95;

    // Bug tracking patterns
    private static readonly string[] BugTrackingPatterns =
    [
        @"https?://github\.com/[\w\-\.]+/[\w\-\.]+/issues",
        @"https?://bugs\.[\w\-\.]+",
        @"https?://[\w\-\.]*jira[\w\-\.]*",
        @"https?://[\w\-\.]*bugzilla[\w\-\.]*",
        @"https?://[\w\-\.]*mantis[\w\-\.]*",
        @"https?://[\w\-\.]*redmine[\w\-\.]*",
        @"https?://[\w\-\.]*trac[\w\-\.]*",
        @"https?://[\w\-\.]*youtrack[\w\-\.]*",
        @"https?://[\w\-\.]*fogbugz[\w\-\.]*",
    ];

    // Repository patterns
    private static readonly string[] RepositoryPatterns =
    [
        @"https?://github\.com/[\w\-\.]+/[\w\-\.]+(?!/issues|/wiki|/releases|/actions|/security|/insights|/settings|/projects|/discussions)",
        @"https?://gitlab\.com/[\w\-\.]+/[\w\-\.]+",
        @"https?://bitbucket\.org/[\w\-\.]+/[\w\-\.]+",
        @"https?://sourceforge\.net/projects/[\w\-\.]+",
        @"https?://code\.google\.com/p/[\w\-\.]+",
        @"https?://launchpad\.net/[\w\-\.]+",
        @"https?://codeplex\.com/[\w\-\.]+",
        @"https?://git\.[\w\-\.]+/[\w\-\.]+/[\w\-\.]+",
        @"https?://[\w\-\.]+\.git\.[\w\-\.]+",
        @"https?://svn\.[\w\-\.]+",
        @"https?://hg\.[\w\-\.]+",
        @"https?://bazaar\.[\w\-\.]+",
        @"https?://fossil\.[\w\-\.]+",
        @"https?://darcs\.[\w\-\.]+",
        @"https?://cvs\.[\w\-\.]+",
    ];

    // Artifact keywords
    private static readonly string[] ArtifactKeywords =
    [
        "artifact",
        "repository",
        "repo",
        "source code",
        "implementation",
        "codebase",
        "project",
        "software",
        "program",
        "application",
        "system",
        "tool",
        "library",
        "framework",
        "dataset",
        "data",
        "benchmark",
        "evaluation",
        "experiment",
        "github",
        "gitlab",
        "bitbucket",
        "sourceforge",
        "available at",
        "can be found",
        "accessible",
        "download",
        "obtain",
        "retrieve",
        "access",
        "provided",
        "supplement",
        "supplementary",
        "material",
        "materials",
        "resource",
        "resources",
        "code",
        "scripts",
        "files",
        "documentation",
        "manual",
        "guide",
        "tutorial",
        "readme",
        "license",
        "copyright",
        "open source",
        "free software",
    ];

    // Artifact sections
    private static readonly string[] ArtifactSections =
    [
        "implementation",
        "code availability",
        "data availability",
        "supplementary material",
        "resources",
        "artifacts",
        "repository",
        "source code",
        "dataset",
        "benchmark",
        "evaluation",
        "experiment",
        "materials",
        "appendix",
    ];

    // Bug list patterns
    private static readonly string[] BugListPatterns =
    [
        @"(?i)bug\s*(?:list|report|track|id|number|#)",
        @"(?i)issue\s*(?:list|report|track|id|number|#)",
        @"(?i)defect\s*(?:list|report|track|id|number|#)",
        @"(?i)fault\s*(?:list|report|track|id|number|#)",
        @"(?i)error\s*(?:list|report|track|id|number|#)",
        @"(?i)problem\s*(?:list|report|track|id|number|#)",
        @"(?i)failure\s*(?:list|report|track|id|number|#)",
        @"(?i)exception\s*(?:list|report|track|id|number|#)",
        @"(?i)crash\s*(?:list|report|track|id|number|#)",
        @"(?i)vulnerability\s*(?:list|report|track|id|number|#)",
        @"(?i)security\s*(?:issue|bug|flaw)",
        @"(?i)patch\s*(?:list|track|id|number|#)",
        @"(?i)fix\s*(?:list|track|id|number|#)",
        @"(?i)ticket\s*(?:list|track|id|number|#)",
    ];

    // Issue number patterns
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
        ExtractDirectUrls(fullText, result);

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

    private void ExtractDirectUrls(string text, BugListDiscoveryResult result)
    {
        ExtractBugTrackingUrls(text.Replace(" ", ""), result);
        ExtractRepositoryUrls(text.Replace(" ", ""), result);
    }

    private void ExtractBugTrackingUrls(string text, BugListDiscoveryResult result)
    {
        foreach (var pattern in BugTrackingPatterns)
        {
            var matches = Regex.Matches(text, pattern, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                var url = CleanUrl(match.Value);
                if (
                    !result.BugLists.Any(b => b.Url.Equals(url, StringComparison.OrdinalIgnoreCase))
                )
                {
                    result.BugLists.Add(
                        new BugListSource
                        {
                            Url = url,
                            Type = DetermineBugListType(url),
                            DiscoveryMethod = "PDF Direct Extraction",
                            Confidence = PdfAnalysisConfidence,
                            TableContext = ExtractUrlContext(url, text),
                        }
                    );
                }
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
                var url = CleanUrl(match.Value);
                if (
                    !result.ArtifactRepositories.Any(r =>
                        r.Url.Equals(url, StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    result.ArtifactRepositories.Add(
                        new ArtifactRepository
                        {
                            Url = url,
                            Type = DetermineRepositoryType(url),
                            DiscoveryMethod = "PDF Direct Extraction",
                            Confidence = PdfAnalysisConfidence,
                        }
                    );
                }
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
        var bugTables = FindBugTables(fullText);

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
                        Type = repository != null ? DetermineBugListType(repository) : "Unknown",
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

    private List<PdfBugTableInfo> FindBugTables(string text)
    {
        var tables = new List<PdfBugTableInfo>();

        foreach (var pattern in BugListPatterns)
        {
            var matches = Regex.Matches(text, pattern, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                var context = ExtractTableContext(text, match.Index);
                tables.Add(
                    new PdfBugTableInfo
                    {
                        Title = match.Value,
                        Content = context,
                        Position = match.Index,
                    }
                );
            }
        }

        return tables
            .GroupBy(t => t.Position / 1000) // Group nearby matches
            .Select(g => g.First())
            .ToList();
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
            foreach (var pattern in RepositoryPatterns)
            {
                var match = Regex.Match(context, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return CleanUrl(match.Value);
                }
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
        var confidence = PdfAnalysisConfidence;

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
        ProcessKeywordSentences(normalizedText, result);
    }

    private async Task ProcessArtifactSections(
        string normalizedText,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        foreach (var section in ArtifactSections)
        {
            var sectionIndex = normalizedText.IndexOf(section);
            if (sectionIndex != -1)
            {
                var sectionContent = ExtractSurroundingContext(normalizedText, section);
                await ProcessUrlsInText(sectionContent, result, paper, cancellationToken);
            }
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
            var url = CleanUrl(match.Value);
            await ProcessFoundUrl(url, text, result, paper, cancellationToken);
        }
    }

    private string CleanUrl(string url)
    {
        // Remove trailing punctuation and whitespace
        return url.TrimEnd('.', ',', ';', ')', ']', '}', ' ', '\t', '\n', '\r');
    }

    private async Task ProcessFoundUrl(
        string url,
        string fullText,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        if (IsRepositoryUrl(url))
        {
            await ProcessRepositoryUrl(url, fullText, result, paper, cancellationToken);
        }
        else if (IsBugTrackingUrl(url))
        {
            if (!result.BugLists.Any(b => b.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
            {
                result.BugLists.Add(
                    new BugListSource
                    {
                        Url = url,
                        Type = DetermineBugListType(url),
                        DiscoveryMethod = "PDF URL Analysis",
                        Confidence = PdfAnalysisConfidence,
                        TableContext = ExtractUrlContext(url, fullText),
                    }
                );
            }
        }
    }

    private bool IsRepositoryUrl(string url)
    {
        return RepositoryPatterns.Any(pattern =>
            Regex.IsMatch(url, pattern, RegexOptions.IgnoreCase)
        );
    }

    private bool IsBugTrackingUrl(string url)
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
        if (
            !result.ArtifactRepositories.Any(r =>
                r.Url.Equals(url, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            var context = ExtractUrlContext(url, fullText);
            var confidence = ContainsArtifactKeywords(context)
                ? KeywordAnalysisConfidence
                : PdfAnalysisConfidence;

            result.ArtifactRepositories.Add(
                new ArtifactRepository
                {
                    Url = url,
                    Type = DetermineRepositoryType(url),
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

    private void ProcessKeywordSentences(string normalizedText, BugListDiscoveryResult result)
    {
        var sentences = normalizedText.Split('.', '!', '?');

        foreach (var sentence in sentences)
        {
            if (ContainsArtifactKeywords(sentence))
            {
                ProcessUrlsInSentence(sentence, result);
            }
        }
    }

    private bool ContainsArtifactKeywords(string sentence)
    {
        return ArtifactKeywords.Any(keyword =>
            sentence.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        );
    }

    private void ProcessUrlsInSentence(string sentence, BugListDiscoveryResult result)
    {
        var urlPattern = @"https?://[^\s<>""']+";
        var matches = Regex.Matches(sentence, urlPattern, RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            var url = CleanUrl(match.Value);
            if (
                IsRepositoryUrl(url)
                && !result.ArtifactRepositories.Any(r =>
                    r.Url.Equals(url, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                result.ArtifactRepositories.Add(
                    new ArtifactRepository
                    {
                        Url = url,
                        Type = DetermineRepositoryType(url),
                        DiscoveryMethod = "PDF Keyword Analysis",
                        Confidence = KeywordAnalysisConfidence,
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
            var messages = new[] { new OpenAI.Chat.UserChatMessage(prompt) };
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

    /// <summary>
    /// Determines the type of bug tracking system based on the URL
    /// </summary>
    private string DetermineBugListType(string url)
    {
        var lowerUrl = url.ToLowerInvariant();

        if (lowerUrl.Contains("github.com") && lowerUrl.Contains("/issues"))
            return "GitHub Issues";
        if (lowerUrl.Contains("jira"))
            return "Jira";
        if (lowerUrl.Contains("bugzilla"))
            return "Bugzilla";
        if (lowerUrl.Contains("mantis"))
            return "MantisBT";
        if (lowerUrl.Contains("redmine"))
            return "Redmine";
        if (lowerUrl.Contains("trac"))
            return "Trac";
        if (lowerUrl.Contains("youtrack"))
            return "YouTrack";
        if (lowerUrl.Contains("fogbugz"))
            return "FogBugz";
        if (lowerUrl.Contains("bugs."))
            return "Custom Bug Tracker";

        return "Unknown Bug Tracker";
    }

    /// <summary>
    /// Determines the type of repository based on the URL
    /// </summary>
    private string DetermineRepositoryType(string url)
    {
        var lowerUrl = url.ToLowerInvariant();

        if (lowerUrl.Contains("github.com"))
            return "GitHub";
        if (lowerUrl.Contains("gitlab.com"))
            return "GitLab";
        if (lowerUrl.Contains("bitbucket.org"))
            return "Bitbucket";
        if (lowerUrl.Contains("sourceforge.net"))
            return "SourceForge";
        if (lowerUrl.Contains("code.google.com"))
            return "Google Code";
        if (lowerUrl.Contains("launchpad.net"))
            return "Launchpad";
        if (lowerUrl.Contains("codeplex.com"))
            return "CodePlex";
        if (lowerUrl.Contains(".git"))
            return "Git Repository";
        if (lowerUrl.Contains("svn."))
            return "Subversion";
        if (lowerUrl.Contains("hg."))
            return "Mercurial";
        if (lowerUrl.Contains("bazaar."))
            return "Bazaar";
        if (lowerUrl.Contains("fossil."))
            return "Fossil";
        if (lowerUrl.Contains("darcs."))
            return "Darcs";
        if (lowerUrl.Contains("cvs."))
            return "CVS";

        return "Unknown Repository";
    }
}
