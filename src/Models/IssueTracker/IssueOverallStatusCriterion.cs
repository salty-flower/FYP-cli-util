using System;
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
    public bool FilterOutNonDeveloperComments { get; set; } = false;

    private const string SystemPrompt = """
        You are an experienced software engineer and open source community contributor,
        assisting researchers in analyzing developer interactions on GitHub.

        **Task:**
        Analyze the given GitHub issue based solely on developer comments and reactions.
        Do not rely on the issue body for judgment, as reporters may not always be accurate or honest.
        You do not have resources to test or reproduce; your conclusions must be drawn strictly from developer responses.

        Step-by-step analysis:
        1. Based on the brief user profile (e.g. is member/collaborator on the repo? is contributor? merged PRs?),
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
            labelInfo.AppendLine();
        }

        if (profile.LabelEvents.Length > 0)
        {
            labelInfo.AppendLine("Label history:");
            foreach (var labelEvent in profile.LabelEvents)
                labelInfo.AppendLine(
                    $"- Label '{labelEvent.Label.Name}' {labelEvent.Event.GetName()} by @{labelEvent.By.Login} at {labelEvent.OccuredAt:s}"
                );
        }

        var commentsInfo = new StringBuilder();
        if (profile.CommentEvents.Length > 0)
        {
            commentsInfo.AppendLine(
                (FilterOutNonDeveloperComments ? "Developer" : "All") + " comments:"
            );
            foreach (var commentEvent in profile.CommentEvents)
                if (
                    !FilterOutNonDeveloperComments
                    || commentEvent.By.IsDeveloper == true
                    || commentEvent.By.IsContributor
                )
                    commentsInfo.AppendLine(
                        $"- @{commentEvent.By.Login} at {commentEvent.Comment.CreatedAt:s}: \"{commentEvent.Comment.Body}\""
                    );
        }
        else
            commentsInfo.AppendLine("No comments for this issue.");

        var otherEventsInfo = new StringBuilder();
        if (profile.OtherEvents.Length > 0)
        {
            otherEventsInfo.AppendLine("Other events:");
            foreach (var otherEvent in profile.OtherEvents)
                otherEventsInfo.AppendLine(
                    $"- {otherEvent.ActorProfile.Login} {otherEvent.EventType} at {otherEvent.OccurredAt:s}: \"{otherEvent.EventDescription}\""
                );
        }

        var users = new HashSet<string>([profile.AuthorProfile.ToString()]);
        foreach (var commentEvent in profile.CommentEvents)
            users.Add(commentEvent.By.ToString());

        foreach (var labelEvent in profile.LabelEvents)
            users.Add(labelEvent.By.ToString());

        var repoInfo = new StringBuilder();
        repoInfo
            .Append($"Repository: {profile.OctokitRepository.Name}. ")
            .Append($"Stars: {profile.OctokitRepository.StargazersCount}. ")
            .Append($"Forks: {profile.OctokitRepository.ForksCount}. ")
            .Append($"Open issues: {profile.OctokitRepository.OpenIssuesCount}. ");

        var prompt = $""""
            <repo_info>{repoInfo}. Today is {DateTime.Today:yyyy-MM-dd}</repo_info>
            <issue_metadata>
            Issue #{profile.OctokitIssue.Number}
            - author: @{profile.AuthorProfile.Login} 
            - created at: {profile.OctokitIssue.CreatedAt:s}
            - status: {(profile.IsClosed ? "closed" : "open")}
            - associated PR: {(
                profile.HasAssociatedPullRequest
                    ? $"#{profile.OctokitIssue.PullRequest.Number} at {profile.OctokitIssue.PullRequest.CreatedAt:s}"
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
            Title: {profile.OctokitIssue.Title}
            """
            {profile.OctokitIssue.Body}
            """
            </issue_content>
            """";

        logger.LogDebug("Issue prompt: {Prompt}", prompt);

        return [new SystemChatMessage(SystemPrompt), new UserChatMessage(prompt)];
    }
}
