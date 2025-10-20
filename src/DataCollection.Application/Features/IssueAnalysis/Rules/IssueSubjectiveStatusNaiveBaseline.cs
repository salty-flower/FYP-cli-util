using System.Net.Http;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using DataCollection.Application.Models.IssueTracker.Profiles;
using DataCollection.Application.Serialization;
using DataCollection.Core.Models.IssueTracker.Responses;
using DataCollection.Infrastructure.Options;
using DataCollection.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace DataCollection.Application.Features.IssueAnalysis.Rules;

public class IssueSubjectiveStatusNaiveBaseline(
    IOptionsSnapshot<LLMOptions> llmOptions,
    ILogger<IssueSubjectiveStatusNaiveBaseline> logger,
    OpenAIClient client,
    IHttpClientFactory httpClientFactory,
    BatchFileHandler batchFileHandler,
    BatchJobPoller batchJobPoller
) : LargeLanguageModelCriterion<IssueProfile, SubjectiveIssueAnalysis>(
        llmOptions.Value.IssueOverallStatusModel,
        logger,
        client,
        httpClientFactory,
        batchFileHandler,
        batchJobPoller
    )
{
    private const string MinimalSystemPrompt = """
        You are analyzing a GitHub issue using raw HTML content. Determine two booleans:
        1. IsRealBug - true if the issue represents a genuine software bug report, false otherwise.
        2. IsDuplicate - true if the issue is marked or discussed as a duplicate, false otherwise.

        Base your decision solely on the provided HTML snippet and respond with JSON that matches the expected schema.
        """;

    private const int MaxHtmlCharacters = 50_000;

    private readonly ILogger<IssueSubjectiveStatusNaiveBaseline> _logger = logger;

    protected override JsonTypeInfo<SubjectiveIssueAnalysis> OutcomeJsonTypeInfo =>
        AppJsonContext.Default.SubjectiveIssueAnalysis;

    protected override IEnumerable<ChatMessage> BuildMessages(IssueProfile profile)
    {
        var owner = profile.SdkRepository.Owner?.Login
            ?? throw new InvalidOperationException("Issue profile missing repository owner login.");
        var repo = profile.SdkRepository.Name
            ?? throw new InvalidOperationException("Issue profile missing repository name.");
        var issueNumber = profile.SdkIssue.Number;

        var rawHtml = FetchRawIssueHtmlAsync(owner, repo, issueNumber).GetAwaiter().GetResult();
        var truncatedHtml = TruncateIfNeeded(rawHtml, owner, repo, issueNumber);

        var userPrompt = new StringBuilder()
            .AppendLine("Raw GitHub issue page HTML:")
            .AppendLine()
            .AppendLine(truncatedHtml)
            .ToString();

        return
        [
            new SystemChatMessage(MinimalSystemPrompt),
            new UserChatMessage(userPrompt),
        ];
    }

    private async Task<string> FetchRawIssueHtmlAsync(string owner, string repo, long issueNumber)
    {
        var issueUrl = $"https://github.com/{owner}/{repo}/issues/{issueNumber}";
        try
        {
            var httpClient = HttpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, issueUrl);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; DataCollection/1.0)");

            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead
            );
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch raw HTML for {Owner}/{Repo}#{IssueNumber} from {Url}", owner, repo, issueNumber, issueUrl);
            throw;
        }
    }

    private string TruncateIfNeeded(string html, string owner, string repo, long issueNumber)
    {
        if (string.IsNullOrEmpty(html))
        {
            _logger.LogWarning(
                "Received empty HTML content for {Owner}/{Repo}#{IssueNumber}",
                owner,
                repo,
                issueNumber
            );
            return string.Empty;
        }

        if (html.Length <= MaxHtmlCharacters)
        {
            return html;
        }

        _logger.LogWarning(
            "HTML content for {Owner}/{Repo}#{IssueNumber} was {Length} characters. Truncating to {Limit} characters.",
            owner,
            repo,
            issueNumber,
            html.Length,
            MaxHtmlCharacters
        );

        return html[..MaxHtmlCharacters] + "\n\n[... truncated ...]";
    }
}
