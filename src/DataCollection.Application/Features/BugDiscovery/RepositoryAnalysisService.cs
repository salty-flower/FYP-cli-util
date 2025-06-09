using System.Text.RegularExpressions;
using DataCollection.Core.Models;
using DataCollection.Infrastructure.Clients;
using DataCollection.Infrastructure.Models.BugList;
using DataCollection.Infrastructure.Models.GitHub;
using DataCollection.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace DataCollection.Application.Features.BugDiscovery;

/// <summary>
/// Service responsible for analyzing repositories to discover bug lists and validate artifact repositories
/// </summary>
public class RepositoryAnalysisService
{
    private readonly GitHubService gitHubService;
    private readonly ILogger<RepositoryAnalysisService> logger;
    private readonly OpenAIClient openAIClient;
    private readonly IOptions<CredentialOptions> credentialOptions;
    private readonly string model;

    // Constants
    private const double RepositoryAnalysisConfidence = 0.85;
    private const int MaxRepositoryRetries = 3;
    private const int RepositoryRetryDelayMs = 1000;

    // Repository patterns
    private static readonly string[] GitHubUrlPatterns =
    [
        @"https?://github\.com/([\w\-\.]+)/([\w\-\.]+)",
        @"https?://www\.github\.com/([\w\-\.]+)/([\w\-\.]+)",
        @"github\.com/([\w\-\.]+)/([\w\-\.]+)",
        @"www\.github\.com/([\w\-\.]+)/([\w\-\.]+)",
    ];

    private static readonly string[] ReadmeFileNames =
    [
        "README.md",
        "readme.md",
        "README.MD",
        "README.txt",
        "readme.txt",
        "README.rst",
        "readme.rst",
        "README",
        "readme",
        "Readme.md",
    ];

    private static readonly string[] BugRelatedPaths =
    [
        "issues",
        "bugs",
        "tickets",
        "defects",
        "problems",
        "errors",
        "failures",
        "exceptions",
        "crashes",
        "vulnerabilities",
    ];

    public RepositoryAnalysisService(
        GitHubService gitHubService,
        ILogger<RepositoryAnalysisService> logger,
        OpenAIClient openAIClient,
        IOptions<CredentialOptions> credentialOptions
    )
    {
        this.gitHubService = gitHubService;
        this.logger = logger;
        this.openAIClient = openAIClient;
        this.credentialOptions = credentialOptions;
        model = "gpt-4o-mini"; // Default model, could be made configurable
    }

    /// <summary>
    /// Explores repositories to find bug lists and validate artifacts
    /// </summary>
    public async Task ExploreRepositoryBugLists(
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        logger.LogInformation("Starting repository exploration for paper {PaperDoi}", paper.Doi);

        var repositories = result.ArtifactRepositories.ToList();
        foreach (var repository in repositories)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await AnalyzeSingleRepository(repository, result, paper, cancellationToken);
        }

        logger.LogInformation(
            "Completed repository exploration for paper {PaperDoi}. Found {BugListCount} bug lists",
            paper.Doi,
            result.BugLists.Count
        );
    }

    private async Task AnalyzeSingleRepository(
        ArtifactRepository repository,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (!IsGitHubUrl(repository.Url))
            {
                logger.LogDebug("Skipping non-GitHub repository: {Url}", repository.Url);
                return;
            }

            var (owner, repoName) = ExtractGitHubOwnerAndRepo(repository.Url);
            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repoName))
            {
                logger.LogWarning("Could not extract owner/repo from URL: {Url}", repository.Url);
                return;
            }

            logger.LogDebug("Analyzing GitHub repository: {Owner}/{RepoName}", owner, repoName);

            // Verify repository accessibility
            if (!await VerifyRepositoryAccess(owner, repoName))
            {
                logger.LogWarning(
                    "Repository {Owner}/{RepoName} is not accessible",
                    owner,
                    repoName
                );
                repository.Confidence *= 0.7; // Reduce confidence for inaccessible repos
                return;
            }

            // Analyze repository for bug lists
            await AnalyzeRepositoryForBugLists(owner, repoName, result, paper, cancellationToken);

            // Analyze README for additional insights
            await AnalyzeRepositoryReadme(owner, repoName, result, paper, cancellationToken);

            // Get repository tree and analyze structure
            await AnalyzeRepositoryStructure(owner, repoName, result, paper, cancellationToken);

            // Boost repository confidence if it contains bug-related content
            repository.Confidence = CalculateRepositoryConfidence(repository, result);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to analyze repository {Url}", repository.Url);
        }
    }

    private bool IsGitHubUrl(string url)
    {
        return GitHubUrlPatterns.Any(pattern =>
            Regex.IsMatch(url, pattern, RegexOptions.IgnoreCase)
        );
    }

    private (string owner, string repoName) ExtractGitHubOwnerAndRepo(string url)
    {
        foreach (var pattern in GitHubUrlPatterns)
        {
            var match = Regex.Match(url, pattern, RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count >= 3)
            {
                return (match.Groups[1].Value, match.Groups[2].Value);
            }
        }
        return ("", "");
    }

    private async Task<bool> VerifyRepositoryAccess(string owner, string repoName)
    {
        try
        {
            var repository = await gitHubService.GetRepositoryInfoAsync(owner, repoName);
            return repository != null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(
                ex,
                "Repository {Owner}/{RepoName} verification failed",
                owner,
                repoName
            );
            return false;
        }
    }

    private async Task AnalyzeRepositoryForBugLists(
        string owner,
        string repoName,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        try
        {
            // Check if repository has issues enabled and get issue count
            var repository = await gitHubService.GetRepositoryInfoAsync(owner, repoName);
            if (repository.HasIssues == true && repository.OpenIssuesCount > 0)
            {
                var issuesUrl = $"https://github.com/{owner}/{repoName}/issues";

                if (
                    !result.BugLists.Any(b =>
                        b.Url.Equals(issuesUrl, StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    var confidence = CalculateIssueConfidence(
                        repository.OpenIssuesCount.GetValueOrDefault()
                    );

                    result.BugLists.Add(
                        new BugListSource
                        {
                            Url = issuesUrl,
                            Type = "GitHub Issues",
                            DiscoveryMethod = "Repository Analysis",
                            Confidence = confidence,
                            TableContext =
                                $"GitHub Issues ({repository.OpenIssuesCount} open issues)",
                        }
                    );

                    logger.LogDebug(
                        "Found active issue tracker for {Owner}/{RepoName} with {IssueCount} open issues",
                        owner,
                        repoName,
                        repository.OpenIssuesCount
                    );
                }
            }

            // Look for bug-related paths in repository structure
            await AnalyzeBugRelatedPaths(owner, repoName, result, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to analyze repository {Owner}/{RepoName} for bug lists",
                owner,
                repoName
            );
        }
    }

    private double CalculateIssueConfidence(int issueCount)
    {
        var confidence = RepositoryAnalysisConfidence;

        // Boost confidence based on issue activity
        if (issueCount > 10)
            confidence += 0.05;
        if (issueCount > 50)
            confidence += 0.05;
        if (issueCount > 100)
            confidence += 0.05;

        return Math.Min(1.0, confidence);
    }

    private async Task AnalyzeBugRelatedPaths(
        string owner,
        string repoName,
        BugListDiscoveryResult result,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var tree = await GetRepositoryTreeWithRetry(owner, repoName, cancellationToken);
            if (tree?.Tree == null)
                return;

            foreach (var item in tree.Tree)
            {
                if (item.Type == "tree" && ContainsBugRelatedKeywords(item.Path))
                {
                    var pathUrl = $"https://github.com/{owner}/{repoName}/tree/main/{item.Path}";

                    if (!result.BugLists.Any(b => b.Url.Contains(item.Path)))
                    {
                        result.BugLists.Add(
                            new BugListSource
                            {
                                Url = pathUrl,
                                Type = "Bug Directory",
                                DiscoveryMethod = "Repository Structure Analysis",
                                Confidence = RepositoryAnalysisConfidence * 0.8, // Lower confidence for structure-based findings
                                TableContext = $"Bug-related directory: {item.Path}",
                            }
                        );
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to analyze bug-related paths for {Owner}/{RepoName}",
                owner,
                repoName
            );
        }
    }

    private bool ContainsBugRelatedKeywords(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        return BugRelatedPaths.Any(keyword =>
            path.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        );
    }

    private async Task<RepositoryTree?> GetRepositoryTreeWithRetry(
        string owner,
        string repoName,
        CancellationToken cancellationToken
    )
    {
        for (int attempt = 1; attempt <= MaxRepositoryRetries; attempt++)
        {
            try
            {
                return await gitHubService.GetRepositoryTreeAsync(owner, repoName);
            }
            catch (Exception ex) when (attempt < MaxRepositoryRetries)
            {
                logger.LogWarning(
                    ex,
                    "Repository tree request failed for {Owner}/{RepoName}, attempt {Attempt}/{MaxRetries}",
                    owner,
                    repoName,
                    attempt,
                    MaxRepositoryRetries
                );

                await Task.Delay(RepositoryRetryDelayMs * attempt, cancellationToken);
            }
        }

        logger.LogError(
            "Failed to get repository tree for {Owner}/{RepoName} after {MaxRetries} attempts",
            owner,
            repoName,
            MaxRepositoryRetries
        );
        return null;
    }

    private async Task AnalyzeRepositoryReadme(
        string owner,
        string repoName,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var readmeContent = await gitHubService.GetRepositoryReadmeAsync(owner, repoName);
            if (string.IsNullOrWhiteSpace(readmeContent))
            {
                logger.LogDebug(
                    "No README found for repository {Owner}/{RepoName}",
                    owner,
                    repoName
                );
                return;
            }

            logger.LogDebug(
                "Analyzing README for repository {Owner}/{RepoName} ({Length} chars)",
                owner,
                repoName,
                readmeContent.Length
            );

            // Extract URLs from README
            await ExtractUrlsFromReadme(readmeContent, result, owner, repoName);

            // Verify artifact relevance with LLM
            await VerifyArtifactRelevanceWithLlm(
                paper,
                readmeContent,
                $"https://github.com/{owner}/{repoName}",
                result,
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to analyze README for {Owner}/{RepoName}",
                owner,
                repoName
            );
        }
    }

    private async Task ExtractUrlsFromReadme(
        string readmeContent,
        BugListDiscoveryResult result,
        string owner,
        string repoName
    )
    {
        var urlPattern = @"https?://[^\s<>\)\]\}""']+";
        var matches = Regex.Matches(readmeContent, urlPattern, RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            var url = CleanUrl(match.Value);

            if (
                IsBugTrackingUrl(url)
                && !result.BugLists.Any(b => b.Url.Equals(url, StringComparison.OrdinalIgnoreCase))
            )
            {
                result.BugLists.Add(
                    new BugListSource
                    {
                        Url = url,
                        Type = DetermineBugListType(url),
                        DiscoveryMethod = "Repository README",
                        Confidence = RepositoryAnalysisConfidence,
                        TableContext = $"Found in {owner}/{repoName} README",
                    }
                );
            }
            else if (
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
                        DiscoveryMethod = "Repository README",
                        Confidence = RepositoryAnalysisConfidence,
                    }
                );
            }
        }
    }

    private string CleanUrl(string url)
    {
        return url.TrimEnd('.', ',', ';', ')', ']', '}', ' ', '\t', '\n', '\r');
    }

    private bool IsBugTrackingUrl(string url)
    {
        var bugTrackingPatterns = new[]
        {
            @"https?://github\.com/[\w\-\.]+/[\w\-\.]+/issues",
            @"https?://bugs\.[\w\-\.]+",
            @"https?://[\w\-\.]*jira[\w\-\.]*",
            @"https?://[\w\-\.]*bugzilla[\w\-\.]*",
        };

        return bugTrackingPatterns.Any(pattern =>
            Regex.IsMatch(url, pattern, RegexOptions.IgnoreCase)
        );
    }

    private bool IsRepositoryUrl(string url)
    {
        var repositoryPatterns = new[]
        {
            @"https?://github\.com/[\w\-\.]+/[\w\-\.]+(?!/issues|/wiki|/releases)",
            @"https?://gitlab\.com/[\w\-\.]+/[\w\-\.]+",
            @"https?://bitbucket\.org/[\w\-\.]+/[\w\-\.]+",
            @"https?://sourceforge\.net/projects/[\w\-\.]+",
        };

        return repositoryPatterns.Any(pattern =>
            Regex.IsMatch(url, pattern, RegexOptions.IgnoreCase)
        );
    }

    private async Task AnalyzeRepositoryStructure(
        string owner,
        string repoName,
        BugListDiscoveryResult result,
        Paper paper,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var tree = await GetRepositoryTreeWithRetry(owner, repoName, cancellationToken);
            if (tree?.Tree == null)
                return;

            var hasTests = tree.Tree.Any(item =>
                item.Path?.Contains("test", StringComparison.OrdinalIgnoreCase) == true
                || item.Path?.Contains("spec", StringComparison.OrdinalIgnoreCase) == true
            );

            var hasDocumentation = tree.Tree.Any(item =>
                item.Path?.Contains("doc", StringComparison.OrdinalIgnoreCase) == true
                || item.Path?.EndsWith(".md", StringComparison.OrdinalIgnoreCase) == true
            );

            var hasBuildFiles = tree.Tree.Any(item =>
                item.Path?.Contains("Makefile", StringComparison.OrdinalIgnoreCase) == true
                || item.Path?.Contains("pom.xml", StringComparison.OrdinalIgnoreCase) == true
                || item.Path?.Contains("package.json", StringComparison.OrdinalIgnoreCase) == true
            );

            // Update repository confidence based on structure indicators
            var artifactRepo = result.ArtifactRepositories.FirstOrDefault(r =>
                r.Url.Contains($"{owner}/{repoName}", StringComparison.OrdinalIgnoreCase)
            );

            if (artifactRepo != null)
            {
                var structureBonus = 0.0;
                if (hasTests)
                    structureBonus += 0.05;
                if (hasDocumentation)
                    structureBonus += 0.03;
                if (hasBuildFiles)
                    structureBonus += 0.02;

                artifactRepo.Confidence = Math.Min(1.0, artifactRepo.Confidence + structureBonus);

                logger.LogDebug(
                    "Repository {Owner}/{RepoName} structure analysis: tests={HasTests}, docs={HasDocs}, build={HasBuild}, confidence={Confidence:F2}",
                    owner,
                    repoName,
                    hasTests,
                    hasDocumentation,
                    hasBuildFiles,
                    artifactRepo.Confidence
                );
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to analyze repository structure for {Owner}/{RepoName}",
                owner,
                repoName
            );
        }
    }

    private double CalculateRepositoryConfidence(
        ArtifactRepository repository,
        BugListDiscoveryResult result
    )
    {
        var baseConfidence = repository.Confidence;

        // Boost confidence if we found bug lists in this repository
        var relatedBugLists = result.BugLists.Count(b =>
            b.Url.Contains(
                repository.Url.Replace("https://github.com/", ""),
                StringComparison.OrdinalIgnoreCase
            )
        );

        if (relatedBugLists > 0)
        {
            baseConfidence += 0.1 * relatedBugLists;
        }

        return Math.Min(1.0, baseConfidence);
    }

    private async Task VerifyArtifactRelevanceWithLlm(
        Paper paper,
        string readmeContent,
        string repositoryUrl,
        BugListDiscoveryResult result,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var truncatedReadme =
                readmeContent.Length > 2000 ? readmeContent.Substring(0, 2000) : readmeContent;

            var prompt = CreateVerificationPrompt(paper, truncatedReadme, repositoryUrl);

            var chatClient = openAIClient.GetChatClient(model);
            var messages = new List<ChatMessage> { new UserChatMessage(prompt) };

            var response = await chatClient.CompleteChatAsync(
                messages,
                cancellationToken: cancellationToken
            );

            if (response?.Value?.Content?.Count > 0)
            {
                var content = response.Value.Content[0].Text;
                if (!string.IsNullOrEmpty(content))
                {
                    await ProcessLlmVerificationResponse(content, repositoryUrl, result);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LLM verification failed for repository {Url}", repositoryUrl);
        }
    }

    private string CreateVerificationPrompt(Paper paper, string readmeContent, string repositoryUrl)
    {
        return $@"Given this research paper and repository, assess if the repository is likely the artifact/implementation for this paper.

Paper Title: {paper.Title}
Paper Authors: {string.Join(", ", paper.Authors ?? [])}
Paper Abstract: {paper.Abstract}

Repository URL: {repositoryUrl}
Repository README: {readmeContent}

Please respond with:
1. RELEVANT or NOT_RELEVANT
2. Confidence score (0.0-1.0)
3. Brief justification (max 100 words)

Format: RELEVANT|0.85|This repository implements the algorithm described in the paper and mentions the same techniques.";
    }

    private async Task ProcessLlmVerificationResponse(
        string response,
        string repositoryUrl,
        BugListDiscoveryResult result
    )
    {
        try
        {
            var parts = response.Split('|');
            if (parts.Length >= 3)
            {
                var relevance = parts[0].Trim().ToUpperInvariant();
                var confidenceStr = parts[1].Trim();
                var justification = parts[2].Trim();

                if (double.TryParse(confidenceStr, out var confidence))
                {
                    var artifactRepo = result.ArtifactRepositories.FirstOrDefault(r =>
                        r.Url.Equals(repositoryUrl, StringComparison.OrdinalIgnoreCase)
                    );

                    if (artifactRepo != null)
                    {
                        if (relevance == "RELEVANT")
                        {
                            artifactRepo.Confidence = Math.Max(artifactRepo.Confidence, confidence);
                            // LLM verified as relevant
                        }
                        else
                        {
                            artifactRepo.Confidence *= 0.5; // Reduce confidence for irrelevant repos
                            // LLM marked as not relevant
                        }

                        logger.LogDebug(
                            "LLM verification for {Url}: {Relevance} (confidence: {Confidence:F2})",
                            repositoryUrl,
                            relevance,
                            confidence
                        );
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to process LLM verification response: {Response}",
                response
            );
        }
    }

    /// <summary>
    /// Verifies a specific artifact URL with context using LLM analysis
    /// </summary>
    public async Task<bool> VerifyArtifactWithContext(
        Paper paper,
        string url,
        string context,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var prompt =
                $@"Given this research paper context and URL, determine if the URL is likely an artifact repository for this paper.

Paper: {paper.Title}
Authors: {string.Join(", ", paper.Authors ?? [])}
Context: {context}
URL: {url}

Respond with only: YES or NO";

            var chatClient = openAIClient.GetChatClient(model);
            var messages = new List<ChatMessage> { new UserChatMessage(prompt) };

            var response = await chatClient.CompleteChatAsync(
                messages,
                cancellationToken: cancellationToken
            );

            return response?.Value?.Content?.Count > 0
                && response.Value.Content[0].Text?.Trim().ToUpperInvariant() == "YES";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to verify artifact with context for URL {Url}", url);
            return false;
        }
    }

    private string DetermineRepositoryType(string url)
    {
        if (url.Contains("github.com"))
            return "GitHub";
        if (url.Contains("gitlab.com"))
            return "GitLab";
        if (url.Contains("bitbucket.org"))
            return "Bitbucket";
        if (url.Contains("sourceforge.net"))
            return "SourceForge";
        return "Repository";
    }

    private string DetermineBugListType(string url)
    {
        if (url.Contains("github.com"))
            return "GitHub Issues";
        if (url.Contains("jira"))
            return "Jira";
        if (url.Contains("bugzilla"))
            return "Bugzilla";
        return "Bug Tracker";
    }
}
