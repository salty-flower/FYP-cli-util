using System;
using System.Net.Http;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using DataCollection.Application.Models.IssueTracker.Profiles;
using DataCollection.Core.Models.IssueTracker.Responses;
using DataCollection.Infrastructure.Options;
using DataCollection.Infrastructure.Serialization;
using HtmlAgilityPack;
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

    protected override JsonTypeInfo<SubjectiveIssueAnalysis> OutcomeJsonTypeInfo =>
        AppJsonContext.Default.SubjectiveIssueAnalysis;

    protected override IEnumerable<ChatMessage> BuildMessages(IssueProfile profile)
    {
        const string systemPrompt = IssueSubjectiveStatusCriterion.TwoFieldSystemPrompt;

        var owner = profile.SdkRepository.Owner?.Login
            ?? throw new InvalidOperationException(
                "Issue profile missing repository owner login."
            );
        var repo = profile.SdkRepository.Name
            ?? throw new InvalidOperationException(
                "Issue profile missing repository name."
            );
        var issueNumber = profile.SdkIssue.Number;

        var issueText = FetchIssuePlainTextAsync(owner, repo, issueNumber)
            .GetAwaiter()
            .GetResult();

        var userPrompt = new StringBuilder()
            .AppendLine("GitHub issue page plain text:")
            .AppendLine()
            .AppendLine(issueText)
            .ToString();

        return
        [
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt),
        ];
    }

    private async Task<string> FetchIssuePlainTextAsync(string owner, string repo, long issueNumber)
    {
        var requestUri = $"https://github.com/{owner}/{repo}/issues/{issueNumber}";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; DataCollection/1.0)");
            request.Headers.Accept.ParseAdd("text/html");

            var httpClient = HttpClientFactory.CreateClient("github-api");
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead
            );

            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(html))
            {
                Logger.LogWarning(
                    "Received empty HTML content for {Owner}/{Repo}#{IssueNumber}",
                    owner,
                    repo,
                    issueNumber
                );
                return string.Empty;
            }

            var document = new HtmlDocument();
            document.LoadHtml(html);
            var innerText = HtmlEntity.DeEntitize(document.DocumentNode.InnerText ?? string.Empty);
            innerText = innerText.Trim();

            if (innerText.Length <= MaxPlainTextCharacters)
            {
                return innerText;
            }

            Logger.LogWarning(
                "Plain text content for {Owner}/{Repo}#{IssueNumber} was {Length} characters. Truncating to {Limit} characters.",
                owner,
                repo,
                issueNumber,
                innerText.Length,
                MaxPlainTextCharacters
            );

            return innerText[..MaxPlainTextCharacters] + "\n\n[... truncated ...]";
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Failed to fetch issue HTML for {Owner}/{Repo}#{IssueNumber} from {Url}",
                owner,
                repo,
                issueNumber,
                requestUri
            );
            throw;
        }
    }

    private const int MaxPlainTextCharacters = Int32.MaxValue;
}
