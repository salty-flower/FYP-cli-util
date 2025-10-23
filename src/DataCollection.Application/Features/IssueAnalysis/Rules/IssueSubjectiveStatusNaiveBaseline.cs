using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
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

    protected override JsonTypeInfo<SubjectiveIssueAnalysis> OutcomeJsonTypeInfo =>
        AppJsonContext.Default.SubjectiveIssueAnalysis;

    protected override IEnumerable<ChatMessage> BuildMessages(IssueProfile profile)
    {
        const string systemPrompt = IssueSubjectiveStatusCriterion.TwoFieldSystemPrompt;

        var issueBody = MinifyMarkdown(profile.SdkIssue.Body);
        var issueTitle = MinifyMarkdown(profile.SdkIssue.Title);

        var commentBodies = profile.CommentEvents
            .Select(static comment => MinifyMarkdown(comment.SdkComment.Body))
            .Where(static body => !string.IsNullOrEmpty(body))
            .Select(static body => body!)
            .ToArray();

        var labelNames = profile.SdkIssue.Labels?
            .Select(static label => label?.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name!.Trim())
            .ToArray();

        var promptPayload = new IssueSubjectiveStatusNaiveBaselinePayload(
            issueTitle,
            issueBody,
            commentBodies.Length > 0 ? commentBodies : null,
            labelNames is { Length: > 0 } ? labelNames : null
        );

        var userPrompt = JsonSerializer.Serialize(
            promptPayload,
            PromptJsonContext.IssueSubjectiveStatusNaiveBaselinePayload
        );

        return
        [
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt)
        ];
    }

    private static string? MinifyMarkdown(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return Regex.Replace(text, "\\s+", " ").Trim();
    }

    private static readonly JsonSerializerOptions PromptSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    private static readonly IssueAnalysisJsonContext PromptJsonContext = new(PromptSerializerOptions);
}

public sealed record IssueSubjectiveStatusNaiveBaselinePayload(
    string? Title,
    string? Body,
    string[]? Comments,
    string[]? Labels
);
