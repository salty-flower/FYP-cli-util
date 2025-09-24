using System.Text;
using System.Text.Json;
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
using OpenAi.JsonSchema.Generator;

namespace DataCollection.Application.Features.IssueAnalysis.Rules;

public class IssueOverallStatusCriterion(
    OpenAIClient client,
    IOptionsSnapshot<LLMOptions> llmOptions,
    ILogger<IssueOverallStatusCriterion> logger,
    IHttpClientFactory httpClientFactory,
    BatchFileHandler batchFileHandler,
    BatchJobPoller batchJobPoller
)
    : LargeLanguageModelCriterion<IssueProfile, IssueAnalysisResponse>(
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
        assisting researchers in analyzing developer interactions on GitHub.

        IMPORTANT: The repository's classification must follow a deterministic decision
        tree based on developer comments, labels, and linked/associated PR metadata.
        This prompt instructs you to synthesize the CONTEXT and EVIDENCE for classification.
        The boolean and nullable fields in the JSON output must reflect the decision rules
        below (do not invent probabilistic judgements). Treat this as a strict rule-based
        mapping from evidence to output values.

        Decision rules (apply in this order):
        1) Identify developers:
           - Consider users with author association roles: Owner, Member, Collaborator, Contributor.
           - Also consider users marked `IsDeveloper == true` in provided user profiles.
           - Collect their usernames: this becomes `DeveloperUsernames` (array).
        2) HasDeveloperJudgement:
           - `true` if at least one developer (as defined above) left a comment that expresses
             an explicit judgement regarding whether the issue is a bug (see step 3).
           - `false` if developers are present but none of their comments express any judgement
             (they only ask for clarification, ask for logs, or reproduce steps).
           - `null` if there are no developer accounts/comments at all.
        3) IsRealBug:
           - `true` if a developer explicitly acknowledges the issue is a bug (e.g. "this is a bug",
             "good catch", "we should fix this").
           - `false` if a developer explicitly states it is not a bug (e.g. "works as intended",
             "not a bug").
           - `null` otherwise (ambiguous comments like "I can reproduce", "interesting" -> inconclusive).
        4) IsFixed:
           - `true` if there exists an associated pull request that is merged and the PR or developer
             comment explicitly links the merge to resolving this issue, or a developer comment says
             the issue has been fixed and a merged PR is present.
           - `false` if developers explicitly say it has not been fixed or there is an explicit
             "won't fix" status and no merged PR linking a fix.
           - `null` if no clear developer statement and no merged PR evidence.
        5) IsFixedBeforeIssueRaised:
           - `true` if the merged PR (evidence for `IsFixed == true`) has a merged timestamp earlier
             than the issue's created timestamp.
           - `false` if the merged PR merged after the issue was created.
           - `null` if `IsFixed` is not `true` or merge timestamp not available.
        6) IsDuplicate:
           - `true` if there is an explicit "duplicate" label added by a developer OR a developer
             comment explicitly states the issue is a duplicate referencing another issue.
           - `false` if a developer explicitly states it is not a duplicate.
           - `null` if no explicit statement or label.
        7) IsBugButWontFix:
           - `true` if a developer explicitly states the bug will not be fixed (e.g. "won't fix",
             "we won't address this").
           - `false` if a developer explicitly rejects that position.
           - `null` otherwise.
        8) IsBugButWaitingForAction:
           - `true` if developers indicate the issue is a bug but action is pending (e.g. "waiting on PR",
             "needstriage", "needs more info" where a maintainer acknowledges the bug but defers).
           - `false` if explicitly not waiting/pending.
           - `null` otherwise.

        Evidence and rationale:
        - For any field that is `true` or `false`, provide a short `*Rationale` string that cites
          the evidence: developer username, quote from the comment or label, and timestamps when relevant.
          Example: "Developer @alice: 'this is a bug' (2023-07-01T12:34:56Z); label 'bug' added by @bob".
        - If the field is `null`, provide a concise explanation of what's missing (e.g. "no developer
          comments explicitly stating bug status").

        Confidence fields:
        - These are deterministic estimates based on the nature of evidence:
            - Explicit developer statements or a merged PR that references the issue -> 0.95
            - Explicit developer negative statements -> 0.95
            - Evidence via labels only (without dev comment) -> 0.75
            - Ambiguous / inconclusive -> 0.5
            - No evidence (field not applicable) -> 0.0
        - Use the numeric values above when mapping evidence to the corresponding Confidence* fields.

        Other constraints:
        - Do NOT use the issue body to make final judgements about whether this is a real bug.
          The issue body may be included in the prompt for reference only; only developer comments,
          labels, events, and PR metadata should be used to make boolean determinations.
        - Keep outputs terse and strictly JSON-serializable to the `IssueAnalysisResponse` schema.
        - Precision and explicit citation of evidence are more important than speculative reasoning.
        - If multiple developers disagree, prefer the explicit developer statement that most directly
          addresses the node (e.g. "this is not a bug" overrides "I can reproduce" for IsRealBug).
        - When in doubt about precedence, follow the decision ordering specified above.

        **Output Instructions:**
        - Your output must be valid JSON matching the `IssueAnalysisResponse` shape.
        - Populate `DeveloperUsernames` with the list of developers detected.
        - Set boolean/null fields according to the decision rules above.
        - Fill every `*Rationale` field with a short evidence string.
        - Populate Confidence fields according to the deterministic mapping above.
        - Do not output any additional fields beyond the `IssueAnalysisResponse` properties.
        """;

    // Secondary system prompt for two-field LLM evaluation:
    // This prompt instructs the LLM to return a minimal JSON containing only the two
    // subjective judgments (and their rationales/confidences). The pipeline will merge
    // those fields into the deterministic context produced earlier.
    private const string TwoFieldSystemPrompt = """
        You are an experienced software engineer helping researchers. For the provided issue,
        evaluate ONLY two subjective questions based strictly on the developer comments,
        label events, and PR metadata supplied in the user message (and the deterministic
        context block if present):

          1) IsRealBug?
          2) IsDuplicate?

        Constraints:
        - Do NOT use the issue body to determine these judgments.
        - Use only the developer-associated comments, label history, and PR metadata included in the prompt.
        - Return ONLY a JSON object with exactly the following six properties:
            - IsRealBug (nullable boolean)
            - WhetherRealBugRationale (string)
            - ConfidenceInWhetherRealBug (number)
            - IsDuplicate (nullable boolean)
            - WhetherDuplicateRationale (string)
            - ConfidenceInWhetherDuplicate (number)
        - Rationales should be concise and cite explicit developer evidence (username and timestamp when present).
        - Use the deterministic confidence mapping:
            - explicit developer statement or linked/merged PR -> 0.95
            - label-only evidence -> 0.75
            - ambiguous/inconclusive -> 0.5
            - no evidence -> 0.0
        - Output ONLY the JSON object (no surrounding commentary or additional fields).
        """;

    // When true, BuildMessages will use the TwoFieldSystemPrompt and include deterministic context
    public bool UseTwoFieldPrompt { get; set; } = false;

    // If provided, this JSON blob will be prepended to the user prompt as deterministic context
    private string? DeterministicContextJson = null;

    /// <summary>
    /// Evaluate only the two subjective fields (IsRealBug, IsDuplicate) using the LLM while
    /// preserving deterministic context for all other fields. The deterministicContext parameter
    /// should be a canonical serialization of fields computed deterministically by the pipeline.
    /// The LLM is expected to return a minimal JSON containing only the two subjective fields
    /// and their rationales/confidences; this method merges that result into the deterministic
    /// IssueAnalysisResponse and returns the merged object.
    /// </summary>
    private record TwoFieldResult
    {
        public bool? IsRealBug { get; init; }
        public string WhetherRealBugRationale { get; init; } = string.Empty;
        public double ConfidenceInWhetherRealBug { get; init; }
        public bool? IsDuplicate { get; init; }
        public string WhetherDuplicateRationale { get; init; } = string.Empty;
        public double ConfidenceInWhetherDuplicate { get; init; }
    }

    public async Task<IssueAnalysisResponse> EvaluateRealBugAndDuplicateAsync(
        IssueProfile profile,
        IssueAnalysisResponse? deterministicContext = null
    )
    {
        // Serialize deterministic context so BuildMessages will include it in the prompt
        DeterministicContextJson =
            deterministicContext != null
                ? JsonSerializer.Serialize(
                    deterministicContext,
                    AppJsonContext.Default.IssueAnalysisResponse
                )
                : null;

        UseTwoFieldPrompt = true;
        try
        {
            // Build messages (BuildMessages will include deterministic context when UseTwoFieldPrompt==true)
            var messages = BuildMessages(profile).ToList();

            // Ask LLM for the minimal two-field schema
            var schema = new DefaultSchemaGenerator()
                .Generate<TwoFieldResult>(new JsonSchemaOptions())
                .ToJson();
            var options = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: "TwoFieldResult-schema",
                    jsonSchema: BinaryData.FromString(schema),
                    jsonSchemaIsStrict: true
                ),
            };

            var chatClient = client.GetChatClient(model);
            var response = await chatClient.CompleteChatAsync(messages, options);

            if (response.Value.Content.Count == 0)
                throw new InvalidOperationException(
                    "No choices returned from LLM for two-field evaluation."
                );

            var resultText = response.Value.Content[0].Text;
            if (string.IsNullOrEmpty(resultText))
                throw new InvalidOperationException(
                    "Empty result from LLM for two-field evaluation."
                );

            var llmResult =
                JsonSerializer.Deserialize<TwoFieldResult>(
                    resultText,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                ) ?? new TwoFieldResult();

            // Prepare base deterministic object to merge into
            var baseDeterministic =
                deterministicContext
                ?? new IssueAnalysisResponse
                {
                    DeveloperUsernames =
                        deterministicContext?.DeveloperUsernames ?? Array.Empty<string>(),
                    HasDeveloperJudgement = deterministicContext?.HasDeveloperJudgement ?? false,
                    IsRealBug = null,
                    WhetherRealBugRationale = string.Empty,
                    ConfidenceInWhetherRealBug = 0.0,
                    IsDuplicate = null,
                    WhetherDuplicateRationale = string.Empty,
                    ConfidenceInWhetherDuplicate = 0.0,
                    IsFixed = deterministicContext?.IsFixed ?? null,
                    IsFixedBeforeIssueRaised =
                        deterministicContext?.IsFixedBeforeIssueRaised ?? null,
                    IsBugButWontFix = deterministicContext?.IsBugButWontFix ?? null,
                    IsBugButWaitingForAction =
                        deterministicContext?.IsBugButWaitingForAction ?? null,
                    NuanceOrExplanation = deterministicContext?.NuanceOrExplanation ?? string.Empty,
                    AdditionalNotes = deterministicContext?.AdditionalNotes,
                };

            // Merge LLM subjective fields into deterministic base
            var merged = baseDeterministic with
            {
                IsRealBug = llmResult.IsRealBug ?? baseDeterministic.IsRealBug,
                WhetherRealBugRationale = string.IsNullOrWhiteSpace(
                    llmResult.WhetherRealBugRationale
                )
                    ? baseDeterministic.WhetherRealBugRationale
                    : llmResult.WhetherRealBugRationale,
                ConfidenceInWhetherRealBug =
                    llmResult.ConfidenceInWhetherRealBug > 0
                        ? llmResult.ConfidenceInWhetherRealBug
                        : baseDeterministic.ConfidenceInWhetherRealBug,

                IsDuplicate = llmResult.IsDuplicate ?? baseDeterministic.IsDuplicate,
                WhetherDuplicateRationale = string.IsNullOrWhiteSpace(
                    llmResult.WhetherDuplicateRationale
                )
                    ? baseDeterministic.WhetherDuplicateRationale
                    : llmResult.WhetherDuplicateRationale,
                ConfidenceInWhetherDuplicate =
                    llmResult.ConfidenceInWhetherDuplicate > 0
                        ? llmResult.ConfidenceInWhetherDuplicate
                        : baseDeterministic.ConfidenceInWhetherDuplicate,

                NuanceOrExplanation =
                    (baseDeterministic.NuanceOrExplanation ?? string.Empty)
                    + " LLM: "
                    + (llmResult.WhetherRealBugRationale ?? string.Empty)
                    + " "
                    + (llmResult.WhetherDuplicateRationale ?? string.Empty),
            };

            return merged;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(
                ex,
                "Two-field LLM evaluation failed; returning deterministic context if available."
            );
            // On failure, return deterministic context if provided, otherwise a minimal deterministic response
            return deterministicContext
                ?? new IssueAnalysisResponse
                {
                    DeveloperUsernames =
                        deterministicContext?.DeveloperUsernames ?? Array.Empty<string>(),
                    HasDeveloperJudgement = deterministicContext?.HasDeveloperJudgement ?? false,
                    IsRealBug = null,
                    WhetherRealBugRationale = string.Empty,
                    ConfidenceInWhetherRealBug = 0.0,
                    IsDuplicate = null,
                    WhetherDuplicateRationale = string.Empty,
                    ConfidenceInWhetherDuplicate = 0.0,
                    IsFixed = deterministicContext?.IsFixed ?? null,
                    IsFixedBeforeIssueRaised =
                        deterministicContext?.IsFixedBeforeIssueRaised ?? null,
                    IsBugButWontFix = deterministicContext?.IsBugButWontFix ?? null,
                    IsBugButWaitingForAction =
                        deterministicContext?.IsBugButWaitingForAction ?? null,
                    NuanceOrExplanation =
                        deterministicContext?.NuanceOrExplanation
                        ?? "Deterministic-only; LLM failed",
                    AdditionalNotes = deterministicContext?.AdditionalNotes,
                };
        }
        finally
        {
            UseTwoFieldPrompt = false;
            DeterministicContextJson = null;
        }
    }

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

        // If operating in two-field LLM mode, prepend deterministic context (if provided)
        if (UseTwoFieldPrompt && !string.IsNullOrEmpty(DeterministicContextJson))
        {
            prompt =
                $"<deterministic_context>\n{DeterministicContextJson}\n</deterministic_context>\n"
                + prompt;
        }

        var sysPrompt = UseTwoFieldPrompt ? TwoFieldSystemPrompt : SystemPrompt;
        return [new SystemChatMessage(sysPrompt), new UserChatMessage(prompt)];
    }
}
