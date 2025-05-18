using System.Collections.Generic;
using System.Text;
using DataCollection.Options;
using EnumsNET;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace DataCollection.Models.IssueTracker;

public class IssueOverallStatusCriterion(
    OpenAIClient client,
    IOptionsSnapshot<LLMOptions> llmOptions,
    ILogger<IssueOverallStatusCriterion> logger
)
    : LargeLanguageModelCriterion<IssueProfile, IssueAnalysisResponse>(
        llmOptions.Value.IssueOverallStatusModel,
        logger,
        client
    )
{
    protected override IEnumerable<ChatMessage> BuildMessages(IssueProfile profile)
    {
        var labelInfo = new StringBuilder();
        if (
            profile.OctokitIssue.Labels is { Count: > 0 }
            && profile.OctokitIssue.Labels.Count != profile.LabelEvents.Length
        // only make sense to show labels again if count mismatch, i.e. some labels were removed
        )
        {
            labelInfo.AppendLine("Issue labels:");
            foreach (var label in profile.OctokitIssue.Labels)
                labelInfo.AppendLine($"- {label.Name}");
        }

        if (profile.LabelEvents.Length > 0)
        {
            labelInfo.AppendLine("\nLabel history:");
            foreach (var labelEvent in profile.LabelEvents)
                labelInfo.AppendLine(
                    $"- Label '{labelEvent.Label.Name}' {labelEvent.Event.GetName()} by {labelEvent.By.Login} (isDeveloper: {labelEvent.By.IsDeveloper}) at {labelEvent.OccuredAt:yyyy-MM-dd HH:mm:ss}"
                );
        }

        var commentsInfo = new StringBuilder();
        if (profile.CommentEvents.Length > 0)
        {
            commentsInfo.AppendLine("Developer comments (most recent first):");
            foreach (var commentEvent in profile.CommentEvents)
                if (commentEvent.By.IsDeveloper == true)
                    commentsInfo.AppendLine(
                        $"- @{commentEvent.By.Login} at {commentEvent.Comment.CreatedAt:yyyy-MM-dd HH:mm:ss}: \"{commentEvent.Comment.Body}\""
                    );
        }
        else
            commentsInfo.AppendLine("No developer comments for this issue.");

        var prompt = $"""
            Issue #{profile.OctokitIssue.Number} at {profile.OctokitIssue.CreatedAt:yyyy-MM-dd HH:mm:ss}: {profile.OctokitIssue.Title}
            Status: {(profile.IsClosed ? "Closed" : "Open")}
            {(
                profile.HasAssociatedPullRequest
                    ? $"Associated PR: {profile.OctokitIssue.PullRequest.Number} at {profile.OctokitIssue.PullRequest.CreatedAt:yyyy-MM-dd HH:mm:ss}"
                    : "No associated PR"
            )}
            {labelInfo}

            {commentsInfo}
            """;

        logger.LogDebug("Issue prompt: {Prompt}", prompt);

        return
        [
            new SystemChatMessage(
                """
                You are a software engineer actively contributing to open source projects and community governance.
                You are now helping researchers to study user-developers' interactions on GitHub.
                Your task is to analyze the given issue, and determine developers' take on it:
                1. Whether the developers think this is a real bug (true) or not a bug (false).
                2. If the issue is closed, whether it has been fixed (true) or deliberately won't be fixed (false).
                3. If it is indeed considered a bug AND fixed, whether the fix was BEFORE or AFTER the issue is raised.
                   In other words, did this issue report contribute to the fix?
                   Similarly, is this issue a duplicate of another issue?

                Note, that you do not have the resource to test or reproduce, so you cannot determine whether a bug is real or not.
                Your judgement is based solely on developers' reactions and comments.
                """
            ),
            new UserChatMessage(prompt),
        ];
    }
}
