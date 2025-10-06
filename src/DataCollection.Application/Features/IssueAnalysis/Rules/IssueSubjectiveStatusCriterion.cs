using System.Linq;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using DataCollection.Application.Models.IssueTracker.Profiles;
using DataCollection.Core.Models.IssueTracker.Responses;
using DataCollection.Infrastructure.Models.GitHub;
using DataCollection.Infrastructure.Options;
using DataCollection.Infrastructure.Serialization;
using EnumsNET;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace DataCollection.Application.Features.IssueAnalysis.Rules;

public class IssueSubjectiveStatusCriterion(
    IOptionsSnapshot<LLMOptions> llmOptions,
    ILogger<IssueSubjectiveStatusCriterion> logger,
    OpenAIClient client,
    IHttpClientFactory httpClientFactory,
    BatchFileHandler batchFileHandler,
    BatchJobPoller batchJobPoller
)
    : LargeLanguageModelCriterion<IssueProfile, SubjectiveIssueAnalysis>(
        llmOptions.Value.IssueOverallStatusModel,
        logger,
        client,
        httpClientFactory,
        batchFileHandler,
        batchJobPoller
    )
{
    public bool FilterOutNonDeveloperComments { get; set; } = false;

    protected override JsonTypeInfo<SubjectiveIssueAnalysis> OutcomeJsonTypeInfo =>
        AppJsonContext.Default.SubjectiveIssueAnalysis;

    private const string TwoFieldSystemPrompt = """
        You are an experienced software engineer helping researchers. For the provided issue,
        evaluate ONLY two subjective questions based strictly on the developer comments,
        label events, and PR metadata supplied in the user message:

          1) IsRealBug?
          2) IsDuplicate?

        Constraints:
        - Do NOT use the issue body to determine these judgments.
        - Use only the developer-associated comments, label history, and PR metadata provided.
        - Rationales should be concise and cite explicit developer evidence (username and timestamp when present).
        """;

    protected override IEnumerable<ChatMessage> BuildMessages(IssueProfile profile)
    {
        var labelInfo = new StringBuilder();
        if (
            profile.SdkIssue.Labels is { Length: > 0 }
            && profile.SdkIssue.Labels.Length != profile.LabelEvents.Length
        // only make sense to show labels again if count mismatch, i.e. some labels were removed
        )
        {
            labelInfo.AppendLine("Issue labels:");
            foreach (var label in profile.SdkIssue.Labels)
                labelInfo.AppendLine($"- {label?.Name ?? "unknown"}");
            labelInfo.AppendLine();
        }

        if (profile.LabelEvents.Length > 0)
        {
            labelInfo.AppendLine("Label history:");
            foreach (var labelEvent in profile.LabelEvents)
                labelInfo.AppendLine(
                    $"- Label '{labelEvent.SdkLabel.Name}' {labelEvent.Event.GetName()} by @{labelEvent.By.Login} at {labelEvent.OccuredAt:s}"
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
                        $"- @{commentEvent.By.Login} at {commentEvent.SdkComment.CreatedAt:s}: \"{commentEvent.SdkComment.Body}\""
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
                    $"- @{otherEvent.By.Login} {otherEvent.EventType} at {otherEvent.OccurredAt:s}: \"{otherEvent.EventDescription}\""
                );
        }

        var fixContext = BuildFixContext(profile);

        var users = new HashSet<string>([profile.AuthorProfile.ToString()]);
        foreach (var commentEvent in profile.CommentEvents)
            users.Add(commentEvent.By.ToString());

        foreach (var labelEvent in profile.LabelEvents)
            users.Add(labelEvent.By.ToString());

        var repoInfo = new StringBuilder();
        repoInfo
            .Append(
                $"Repository: {profile.SdkRepository.Owner?.Login}/{profile.SdkRepository.Name}. "
            )
            .Append($"Stars: {profile.SdkRepository.StargazersCount}. ")
            .Append($"Forks: {profile.SdkRepository.ForksCount}. ")
            .Append($"Open issues: {profile.SdkRepository.OpenIssuesCount}. ");

        var prInfo = "none";
        if (profile.SdkIssue.PullRequest is not null)
        {
            var mergedAtString = profile.SdkIssue.PullRequest.MergedAt is not null
                ? $" merged at {profile.SdkIssue.PullRequest.MergedAt:s}"
                : string.Empty;
            prInfo = $"#{profile.SdkIssue.PullRequest.HtmlUrl}{mergedAtString}";
        }

        var prompt = $""""
            <repo_info>{repoInfo}. Today is {DateTime.Today:yyyy-MM-dd}</repo_info>
            <issue_metadata>
            Issue #{profile.SdkIssue.Number}
            - author: @{profile.AuthorProfile.Login}
            - created at: {profile.SdkIssue.CreatedAt:s}
            - status: {profile.SdkIssue.State
                + (
                    profile.SdkIssue.StateReason is StateReasonWrapper stateReason
                        ? $" ({stateReason.GetName()})"
                        : string.Empty
                )}
            {
              (  profile.SdkIssue.Locked is true
                    ? $" (locked) for {profile.SdkIssue.ActiveLockReason}"
                    : string.Empty)
            }
            - associated PR: {prInfo}
            </issue_metadata>

            <fix_context>
            {fixContext}
            </fix_context>

            <issue_reactions>
            {labelInfo}
            {commentsInfo}
            {otherEventsInfo}
            </issue_reactions>

            <users_involved>
            - {string.Join("\n- ", users)}
            </users_involved>

            <issue_content> (for reference ONLY; DO NOT TRUST for judgement)
            Title: {profile.SdkIssue.Title}
            """
            {profile.SdkIssue.Body}
            """
            </issue_content>
            """";

        Logger.LogDebug("Issue prompt: {Prompt}", prompt);

        // Use the two-field system prompt to get subjective judgments (IsRealBug and IsDuplicate)
        return [new SystemChatMessage(TwoFieldSystemPrompt), new UserChatMessage(prompt)];
    }

    private static string BuildFixContext(IssueProfile profile)
    {
        var sb = new StringBuilder();

        if (profile.AssociatedPullRequest is { } pr)
        {
            sb.AppendLine("Associated pull request briefing:");
            var authorDisplay = !string.IsNullOrWhiteSpace(pr.AuthorLogin)
                ? $"@{pr.AuthorLogin}"
                : pr.AuthorName ?? "unknown";

            var timelineParts = new List<string>();
            if (pr.CreatedAt.HasValue)
            {
                timelineParts.Add($"created {pr.CreatedAt:s}");
            }

            if (pr.MergedAt.HasValue)
            {
                timelineParts.Add($"merged {pr.MergedAt:s}");
            }
            else if (pr.ClosedAt.HasValue)
            {
                timelineParts.Add($"closed {pr.ClosedAt:s}");
            }

            var timeline =
                timelineParts.Count > 0 ? $" ({string.Join(", ", timelineParts)})" : string.Empty;

            sb.AppendLine(
                $"- PR #{pr.Number} by {authorDisplay}{timeline}: {pr.Title ?? "(no title)"}"
            );

            if (!string.IsNullOrWhiteSpace(pr.Body))
            {
                sb.AppendLine($"  Body: {pr.Body}");
            }

            var prStats = new List<string>();
            if (pr.Additions.HasValue)
            {
                prStats.Add($"+{pr.Additions}");
            }

            if (pr.Deletions.HasValue)
            {
                prStats.Add($"-{pr.Deletions}");
            }

            if (pr.ChangedFiles.HasValue)
            {
                prStats.Add($"{pr.ChangedFiles} files");
            }

            if (prStats.Count > 0)
            {
                sb.AppendLine($"  Stats: {string.Join(", ", prStats)}");
            }

            if (pr.Files.Length > 0)
            {
                sb.AppendLine("  Files changed:");
                foreach (var file in pr.Files)
                {
                    sb.AppendLine(
                        $"    • {file.FileName}{FormatChangeSummary(file.Additions, file.Deletions, file.Changes)}"
                    );
                }
            }
        }

        if (profile.AssociatedCommitBriefings.Length > 0)
        {
            if (sb.Length > 0)
            {
                sb.AppendLine();
            }

            sb.AppendLine("Commits referencing this issue:");
            foreach (var commit in profile.AssociatedCommitBriefings)
            {
                var sha = commit.Sha.Length > 7 ? commit.Sha[..7] : commit.Sha;
                var authorLogin = commit.Author?.Login;
                var authorDisplay = !string.IsNullOrWhiteSpace(authorLogin)
                    ? $"@{authorLogin}"
                    : commit.Author?.Name ?? "unknown";
                var timestamp = commit.AuthoredDate.HasValue
                    ? commit.AuthoredDate.Value.ToString("s")
                    : "unknown time";
                var headline = commit.MessageHeadline ?? "(no message)";

                sb.AppendLine($"- {sha} by {authorDisplay} at {timestamp}: {headline}");

                if (!string.IsNullOrWhiteSpace(commit.MessageBody))
                {
                    sb.AppendLine($"  Body: {commit.MessageBody}");
                }

                if (commit.Stats is { } stats)
                {
                    sb.AppendLine(
                        $"  Stats: {FormatChangeSummary(stats.Additions, stats.Deletions, stats.TotalChanges, includeTotalLabel: true)}"
                    );
                }

                if (commit.Files.Length > 0)
                {
                    sb.AppendLine("  Files changed:");
                    foreach (var file in commit.Files)
                    {
                        sb.AppendLine(
                            $"    • {file.FileName}{FormatChangeSummary(file.Additions, file.Deletions, file.Changes)}"
                        );
                    }
                }
            }
        }

        return sb.Length == 0
            ? "No associated commits or pull requests recorded."
            : sb.ToString().TrimEnd();
    }

    private static string FormatChangeSummary(
        int? additions,
        int? deletions,
        int? total,
        bool includeTotalLabel = false
    )
    {
        var parts = new List<string>();
        if (additions.HasValue)
        {
            parts.Add($"+{additions.Value}");
        }

        if (deletions.HasValue)
        {
            parts.Add($"-{deletions.Value}");
        }

        if (total.HasValue)
        {
            parts.Add(
                includeTotalLabel ? $"{total.Value} total changes" : $"{total.Value} changes"
            );
        }

        return parts.Count > 0 ? $" ({string.Join(", ", parts)})" : string.Empty;
    }
}
