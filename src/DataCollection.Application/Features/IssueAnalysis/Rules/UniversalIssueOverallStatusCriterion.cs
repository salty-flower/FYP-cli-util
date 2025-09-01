using System.Text;
using System.Text.Json.Serialization.Metadata;
using DataCollection.Core.Models.IssueTracker;
using DataCollection.Core.Models.IssueTracker.Responses;
using DataCollection.Infrastructure.Options;
using DataCollection.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace DataCollection.Application.Features.IssueAnalysis.Rules;

public class UniversalIssueOverallStatusCriterion(
    OpenAIClient client,
    IOptionsSnapshot<LLMOptions> llmOptions,
    ILogger<UniversalIssueOverallStatusCriterion> logger,
    IHttpClientFactory httpClientFactory,
    BatchFileHandler batchFileHandler,
    BatchJobPoller batchJobPoller
)
    : LargeLanguageModelCriterion<UniversalIssueProfile, IssueAnalysisResponse>(
        llmOptions.Value.IssueOverallStatusModel,
        logger,
        client,
        httpClientFactory,
        batchFileHandler,
        batchJobPoller
    )
{
    public bool FilterOutNonDeveloperComments { get; set; } = false;

    protected override JsonTypeInfo<IssueAnalysisResponse> OutcomeJsonTypeInfo =>
        AppJsonContext.Default.IssueAnalysisResponse;

    private const string SystemPrompt = """
        You are an experienced software engineer and open source community contributor,
        assisting researchers in analyzing developer interactions across various issue tracking systems 
        (GitHub, Jira, Bugzilla, etc.).

        **Task:**
        Analyze the given issue based solely on developer comments and reactions.
        Do not rely on the issue body for judgment, as reporters may not always be accurate or honest.
        You do not have resources to test or reproduce; your conclusions must be drawn strictly from developer responses.

        Step-by-step analysis:
        1. Based on the brief user profile (e.g. is maintainer/committer/triage owner? is contributor?),
           determine which users are developers.
        2. Are there developer accounts? Did they give any judgement on this issue?
            - If yes:
                2. Do developers consider this a real bug?
                    - If yes:
                        3. Is the issue fixed?
                            - If yes:
                                * Was the fix applied before or after the issue was raised?
                            - If no:
                                * Is it mentioned as a duplicate of another issue?
                                * Did developers state they chose not to fix it?
            - If no:
                * Consider the time since the issue was raised, and time till NOW. Are there side-channel info?
        **Output Instructions:**
        - Your output should be in JSON.
        - For those "nullable boolean" fields:
            - `true` if developer comments CLEARLY support "yes".
            - `false` if CLEARLY  "no".
            - `null` if
                - this field does not apply (e.g. "IsFixed", "IsBugButWontFix" for not a bug)
                - current developer action/comments is missing/insufficient, thus inconclusive
        - Do not use or reference the issue body for your judgments.
        - Precision is valued over completeness.
        """;

    protected override IEnumerable<ChatMessage> BuildMessages(UniversalIssueProfile profile)
    {
        var labelInfo = new StringBuilder();

        // Show current labels
        if (profile.CoreIssue.Labels is { Count: > 0 })
        {
            labelInfo.AppendLine("Issue labels:");
            foreach (var label in profile.CoreIssue.Labels)
                labelInfo.AppendLine($"- {label}");
            labelInfo.AppendLine();
        }

        // Show label events from universal events
        var labelEvents = profile
            .Events.Where(e => e.EventType == "labeled" || e.EventType == "unlabeled")
            .ToList();
        if (labelEvents.Count > 0)
        {
            labelInfo.AppendLine("Label history:");
            foreach (var labelEvent in labelEvents)
                labelInfo.AppendLine(
                    $"- {labelEvent.EventType} by @{labelEvent.Actor} at {labelEvent.OccurredAt:s}: {labelEvent.Description}"
                );
        }

        var commentsInfo = new StringBuilder();
        if (profile.Comments.Count > 0)
        {
            commentsInfo.AppendLine(
                (FilterOutNonDeveloperComments ? "Developer" : "All") + " comments:"
            );

            foreach (var comment in profile.Comments)
            {
                var authorProfile = profile.Participants.FirstOrDefault(p =>
                    p.Username == comment.Author
                );

                if (
                    !FilterOutNonDeveloperComments
                    || authorProfile?.IsDeveloper == true
                    || authorProfile?.IsContributor == true
                    || authorProfile?.IsMaintainer == true
                    || authorProfile?.IsCommitter == true
                    || authorProfile?.IsTriageOwner == true
                )
                {
                    commentsInfo.AppendLine(
                        $"- @{comment.Author} at {comment.CreatedAt:s}: \"{comment.Content}\""
                    );
                }
            }
        }
        else
            commentsInfo.AppendLine("No comments for this issue.");

        var otherEventsInfo = new StringBuilder();
        var nonLabelEvents = profile
            .Events.Where(e => e.EventType != "labeled" && e.EventType != "unlabeled")
            .ToList();
        if (nonLabelEvents.Count > 0)
        {
            otherEventsInfo.AppendLine("Other events:");
            foreach (var otherEvent in nonLabelEvents)
                otherEventsInfo.AppendLine(
                    $"- @{otherEvent.Actor} {otherEvent.EventType} at {otherEvent.OccurredAt:s}: \"{otherEvent.Description}\""
                );
        }

        var users = new HashSet<string> { profile.AuthorProfile.ToPromptString() };
        foreach (var participant in profile.Participants)
            users.Add(participant.ToPromptString());

        var repoInfo = new StringBuilder();
        repoInfo.Append(
            $"Repository: {profile.CoreRepository.Name} ({profile.CoreRepository.Provider}). "
        );
        if (!string.IsNullOrEmpty(profile.CoreRepository.Description))
            repoInfo.Append($"Description: {profile.CoreRepository.Description}. ");

        // Provider-agnostic issue metadata
        var statusInfo = profile.CoreIssue.Status.ToString();
        if (profile.CoreIssue.ClosedAt.HasValue)
            statusInfo += $" (closed at {profile.CoreIssue.ClosedAt.Value:s})";

        var prompt = $""""
            <repo_info>{repoInfo}Today is {DateTime.Today:yyyy-MM-dd}</repo_info>
            <issue_metadata>
            Issue {profile.CoreIssue.Id}
            - author: @{profile.AuthorProfile.Username} 
            - created at: {profile.CoreIssue.CreatedAt:s}
            - status: {statusInfo}
            - priority: {profile.CoreIssue.Priority ?? "not set"}
            - severity: {profile.CoreIssue.Severity ?? "not set"}
            - assignees: {(
                profile.CoreIssue.Assignees.Count > 0
                    ? string.Join(", ", profile.CoreIssue.Assignees.Select(a => $"@{a}"))
                    : "none"
            )}
            </issue_metadata>

            <issue_reactions>
            {labelInfo}
            {commentsInfo}
            {otherEventsInfo}
            </issue_reactions>

            <users_involved>
            - {string.Join("\n- ", users)}
            </users_involved>

            <issue_content> (for reference ONLY; DO NOT TRUST for judgement)
            Title: {profile.CoreIssue.Title}
            """
            {profile.CoreIssue.Description ?? "No description provided"}
            """
            </issue_content>
            """";

        Logger.LogDebug("Issue prompt: {Prompt}", prompt);

        return [new SystemChatMessage(SystemPrompt), new UserChatMessage(prompt)];
    }
}
