using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DataCollection.Application.Features.SemanticAgents.Models;
using DataCollection.Application.Features.SemanticAgents.Tools;
using DataCollection.Application.Serialization;
using DataCollection.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using OpenAi.JsonSchema.Generator;
using OpenAi.JsonSchema.Serialization;

namespace DataCollection.Application.Features.SemanticAgents;

public interface IDiscoveryAgent
{
    Task<AgentPlan> PlanTaskAsync(DiscoveryTask task);

    [RequiresUnreferencedCode(
        "Calls ExecuteActionAsync which calls methods that require unreferenced code."
    )]
    [RequiresDynamicCode("Calls ExecuteActionAsync which calls methods that require dynamic code.")]
    Task<List<DiscoveryResult>> ExecutePlanAsync(AgentPlan plan, DiscoveryTask task);
    Task<AgentReflection> ReflectOnResultsAsync(
        DiscoveryTask task,
        AgentPlan plan,
        List<DiscoveryResult> results
    );

    [RequiresUnreferencedCode(
        "Calls ExecuteActionAsync which calls methods that require unreferenced code."
    )]
    [RequiresDynamicCode("Calls ExecuteActionAsync which calls methods that require dynamic code.")]
    Task<List<DiscoveryResult>> DiscoverAsync(DiscoveryTask task);
}

public class DiscoveryAgent(
    OpenAIClient openAIClient,
    DiscoveryTools discoveryTools,
    IOptionsSnapshot<LLMOptions> llmOptions,
    ILogger<DiscoveryAgent> logger
) : IDiscoveryAgent
{
    private readonly LLMOptions _llmOptions = llmOptions.Value;

    [RequiresUnreferencedCode(
        "Calls ExecuteActionAsync which calls methods that require unreferenced code."
    )]
    [RequiresDynamicCode("Calls ExecuteActionAsync which calls methods that require dynamic code.")]
    public async Task<List<DiscoveryResult>> DiscoverAsync(DiscoveryTask task)
    {
        logger.LogInformation(
            "Starting autonomous discovery for task {TaskId}: {Description}",
            task.Id,
            task.Description
        );

        try
        {
            // Phase 1: Planning
            var plan = await PlanTaskAsync(task);
            logger.LogInformation(
                "Generated plan with {ActionCount} actions for task {TaskId}",
                plan.Actions.Count,
                task.Id
            );

            // Phase 2: Execution
            var results = await ExecutePlanAsync(plan, task);
            logger.LogInformation(
                "Executed plan and found {ResultCount} results for task {TaskId}",
                results.Count,
                task.Id
            );

            // Phase 3: Reflection
            var reflection = await ReflectOnResultsAsync(task, plan, results);
            logger.LogInformation(
                "Reflection completed for task {TaskId}. Overall confidence: {Confidence:F2}",
                task.Id,
                reflection.OverallConfidence
            );

            return results;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to complete discovery task {TaskId}", task.Id);
            return [];
        }
    }

    public async Task<AgentPlan> PlanTaskAsync(DiscoveryTask task)
    {
        var systemPrompt = """
            You are an autonomous research agent specialized in discovering bug lists, artifact repositories, and vulnerabilities from academic papers and documentation.

            AVAILABLE TOOLS:
            1. SearchTextPatterns - Search for patterns in text using regex and keywords
            2. ExtractUrls - Extract and validate URLs from text content  
            3. AnalyzeUrlContext - Analyze context around URLs for relevance
            4. ValidateUrls - Check if URLs are accessible and extract metadata
            5. ScoreResults - Score and rank discovery results
            6. SearchGitHubRepositories - Search GitHub repositories with multi-round adaptive search using GitHub SDK
            7. SearchWeb - Search the web for bug lists, datasets, or artifacts
            8. SearchZenodo - Search Zenodo for research datasets and artifacts

            Create a step-by-step plan with logical reasoning. Consider these strategies:
            - Start with broad text pattern searches if content is provided
            - PRIORITIZE GitHub repository search as your PRIMARY external search method
            - Use GitHub SDK search with paper metadata (DOI, title, authors) for targeted discovery
            - GitHub search should use paper-specific terms, NOT generic terms like "github zenodo figshare"
            - Use web search ONLY if GitHub search finds insufficient results
            - Use Zenodo search specifically for research artifacts and datasets
            - Extract and validate URLs from any discovered content
            - Score and rank all results based on relevance and confidence
            - Focus on the specific discovery type requested

            SEARCH PRIORITY ORDER:
            1. GitHub SDK search with paper metadata (most reliable)
            2. Text pattern analysis for embedded URLs
            3. Web search only as fallback (least reliable)

            IMPORTANT: For GitHub searches, use paper-specific information from task metadata:
            - Search with DOI directly: Extract DOI from metadata and search for it
            - Search with paper title terms: Extract key terms from paper_title metadata
            - Search with research domain: Use research_domain metadata if available
            - Avoid generic terms like "github zenodo figshare artifact"

            Return your response as structured JSON matching the required schema.
            """;

        var userPrompt = $"""
            TASK TO PLAN:
            - Type: {task.Type}
            - Description: {task.Description}
            - Source: {task.Source}
            - Keywords: {string.Join(", ", task.Keywords)}
            - Paper Metadata: {JsonSerializer.Serialize(
                task.Metadata,
                SemanticAgentsJsonContext.Default.DictionaryStringString
            )}

            Create a detailed plan for discovering {task.Type} from the provided source content.

            IMPORTANT: If paper metadata is available (DOI, title, authors), prioritize using this specific information for external searches rather than generic terms. This will dramatically improve discovery accuracy and find paper-specific artifacts and implementations.
            """;

        try
        {
            var schema = new DefaultSchemaGenerator()
                .Generate<AgentPlan>(new JsonSchemaOptions())
                .ToJson();

            var options = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: "agent-plan-schema",
                    jsonSchema: BinaryData.FromString(schema),
                    jsonSchemaIsStrict: true
                ),
                MaxOutputTokenCount = 5000,
            };

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(userPrompt),
            };

            var planningChatClient = openAIClient.GetChatClient(_llmOptions.AgentPlanningModel);
            var response = await planningChatClient.CompleteChatAsync(messages, options);

            if (response.Value.Content.Count == 0)
                throw new InvalidOperationException("No response from planning model.");

            var planJson = response.Value.Content[0].Text;
            if (string.IsNullOrEmpty(planJson))
                throw new InvalidOperationException("Empty response from planning model.");

            var plan = JsonSerializer.Deserialize(
                planJson,
                SemanticAgentsJsonContext.Default.AgentPlan
            );

            return plan
                ?? new AgentPlan
                {
                    TaskId = task.Id,
                    Actions = [],
                    Reasoning = "Failed to deserialize plan",
                    ExpectedOutcomes = [],
                    Parameters = [],
                };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate plan for task {TaskId}", task.Id);
            return new AgentPlan
            {
                TaskId = task.Id,
                Actions = [],
                Reasoning = $"Failed to generate plan: {ex.Message}",
                ExpectedOutcomes = [],
                Parameters = [],
            };
        }
    }

    [RequiresUnreferencedCode(
        "Calls ExecuteActionAsync which calls methods that require unreferenced code."
    )]
    [RequiresDynamicCode("Calls ExecuteActionAsync which calls methods that require dynamic code.")]
    public async Task<List<DiscoveryResult>> ExecutePlanAsync(AgentPlan plan, DiscoveryTask task)
    {
        var actionResults = new Dictionary<string, string>();
        var allResults = new List<DiscoveryResult>();

        // Execute actions in dependency order
        var executedActions = new HashSet<string>();

        while (executedActions.Count < plan.Actions.Count)
        {
            var readyActions = plan
                .Actions.Where(a => !executedActions.Contains(a.Id))
                .Where(a => a.Dependencies.All(dep => executedActions.Contains(dep)))
                .ToList();

            if (!readyActions.Any())
            {
                logger.LogWarning(
                    "Circular dependency or unresolvable dependencies in plan for task {TaskId}",
                    task.Id
                );
                break;
            }

            foreach (var action in readyActions)
            {
                try
                {
                    logger.LogDebug(
                        "Executing action {ActionId}: {ActionType}",
                        action.Id,
                        action.ActionType
                    );

                    var result = await ExecuteActionAsync(action, actionResults, task);
                    actionResults[action.Id] = result;
                    executedActions.Add(action.Id);

                    // Parse results if they contain discovery results
                    if (action.ActionType.Contains("Search") || action.ActionType.Contains("Score"))
                    {
                        try
                        {
                            var parsedResults = JsonSerializer.Deserialize(
                                result,
                                SemanticAgentsJsonContext.Default.ListDiscoveryResult
                            );
                            if (parsedResults != null)
                            {
                                allResults.AddRange(parsedResults);
                            }
                        }
                        catch (JsonException)
                        {
                            // Not a discovery result, continue
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to execute action {ActionId} for task {TaskId}",
                        action.Id,
                        task.Id
                    );
                    actionResults[action.Id] = $"{{\"error\": \"{ex.Message}\"}}";
                    executedActions.Add(action.Id); // Mark as executed to prevent infinite loop
                }
            }
        }

        return allResults.Distinct().ToList();
    }

    public async Task<AgentReflection> ReflectOnResultsAsync(
        DiscoveryTask task,
        AgentPlan plan,
        List<DiscoveryResult> results
    )
    {
        var reflectionPrompt = $"""
            ORIGINAL TASK:
            - Type: {task.Type}
            - Description: {task.Description}
            - Expected to find: {string.Join(", ", plan.ExpectedOutcomes)}

            EXECUTION SUMMARY:
            - Planned actions: {plan.Actions.Count}
            - Results found: {results.Count}
            - Average confidence: {(results.Any() ? results.Average(r => r.Confidence) : 0):F2}

            RESULTS OVERVIEW:
            {JsonSerializer.Serialize(
                results.Take(5),
                SemanticAgentsJsonContext.Default.IEnumerableDiscoveryResult
            )}

            Analyze the execution and provide reflection focusing on:
            - Quality and relevance of discovered URLs/artifacts
            - Effectiveness of the search strategy
            - Areas for improvement
            """;

        try
        {
            var schema = new DefaultSchemaGenerator()
                .Generate<AgentReflection>(new JsonSchemaOptions())
                .ToJson();

            var options = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: "agent-reflection-schema",
                    jsonSchema: BinaryData.FromString(schema),
                    jsonSchemaIsStrict: true
                ),
                MaxOutputTokenCount = 2000,
            };

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(
                    "You are reflecting on the execution of a discovery task. Analyze the results and provide insights using the specified JSON schema."
                ),
                new UserChatMessage(reflectionPrompt),
            };

            var reflectionChatClient = openAIClient.GetChatClient(_llmOptions.AgentReflectionModel);
            var response = await reflectionChatClient.CompleteChatAsync(messages, options);

            if (response.Value.Content.Count == 0)
                throw new InvalidOperationException("No response from reflection model.");

            var reflectionJson = response.Value.Content[0].Text;
            if (string.IsNullOrEmpty(reflectionJson))
                throw new InvalidOperationException("Empty response from reflection model.");

            var reflection = JsonSerializer.Deserialize(
                reflectionJson,
                SemanticAgentsJsonContext.Default.AgentReflection
            );

            return reflection
                ?? new AgentReflection
                {
                    TaskId = task.Id,
                    Summary = "Failed to deserialize reflection",
                    SuccessfulActions = [],
                    FailedActions = [],
                    LessonsLearned = [],
                    ImprovementSuggestions = [],
                    OverallConfidence = 0.0,
                    NextSteps = [],
                };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate reflection for task {TaskId}", task.Id);
            return new AgentReflection
            {
                TaskId = task.Id,
                Summary = "Reflection generation failed",
                SuccessfulActions = [],
                FailedActions = ["reflection_generation"],
                LessonsLearned = ["Improve error handling for reflection generation"],
                ImprovementSuggestions = ["Add better JSON schema validation"],
                OverallConfidence = results.Any() ? results.Average(r => r.Confidence) : 0.0,
                NextSteps = [],
            };
        }
    }

    [RequiresUnreferencedCode("Calls methods that require unreferenced code.")]
    [RequiresDynamicCode("Calls methods that require dynamic code.")]
    private async Task<string> ExecuteActionAsync(
        AgentAction action,
        Dictionary<string, string> previousResults,
        DiscoveryTask task
    )
    {
        // Prepare parameters, substituting results from previous actions
        var parameters = SubstituteParameters(action.Parameters, previousResults, task);

        return action.Tool.ToLowerInvariant() switch
        {
            "searchtextpatterns" => discoveryTools.SearchTextPatterns(
                parameters.GetValueOrDefault("content", task.Source) ?? "",
                parameters.GetValueOrDefault("patternType", task.Type.ToString()) ?? "",
                parameters.GetValueOrDefault("keywords", string.Join(",", task.Keywords)) ?? ""
            ),

            "extracturls" => await discoveryTools.ExtractUrls(
                parameters.GetValueOrDefault("content", task.Source) ?? ""
            ),

            "analyzeurlcontext" => await discoveryTools.AnalyzeUrlContext(
                parameters.GetValueOrDefault("content", task.Source) ?? "",
                parameters.GetValueOrDefault("url", "") ?? "",
                int.TryParse(parameters.GetValueOrDefault("contextWords", "20"), out var words)
                    ? words
                    : 20
            ),

            "validateurls" => await discoveryTools.ValidateUrls(
                parameters.GetValueOrDefault("urlsJson", "[]") ?? "[]"
            ),

            "scoreresults" => await discoveryTools.ScoreResults(
                parameters.GetValueOrDefault("resultsJson", "[]") ?? "[]",
                parameters.GetValueOrDefault("validationJson", "") ?? ""
            ),

            "searchgithubrepositories" => await discoveryTools.SearchGitHubRepositories(
                GetEffectiveKeywords(parameters, task),
                int.TryParse(parameters.GetValueOrDefault("maxResults", "10"), out var maxResults)
                    ? maxResults
                    : 10,
                parameters.GetValueOrDefault("language", "") ?? "",
                parameters.GetValueOrDefault("qualifiers", "") ?? "",
                bool.TryParse(
                    parameters.GetValueOrDefault("multiRoundSearch", "true"),
                    out var multiRound
                )
                    ? multiRound
                    : true,
                task.Metadata
            ),

            "searchweb" => await discoveryTools.SearchWeb(
                GetEffectiveKeywords(parameters, task, "query"),
                int.TryParse(
                    parameters.GetValueOrDefault("maxResults", "10"),
                    out var webMaxResults
                )
                    ? webMaxResults
                    : 10,
                parameters.GetValueOrDefault("siteFilter", "") ?? ""
            ),

            "searchzenodo" => await discoveryTools.SearchZenodo(
                GetEffectiveKeywords(parameters, task, "query"),
                int.TryParse(
                    parameters.GetValueOrDefault("maxResults", "10"),
                    out var zenodoMaxResults
                )
                    ? zenodoMaxResults
                    : 10,
                parameters.GetValueOrDefault("resourceType", "") ?? ""
            ),

            _ => $"{{\"error\": \"Unknown tool: {action.Tool}\"}}",
        };
    }

    private static Dictionary<string, string> SubstituteParameters(
        Dictionary<string, object> actionParameters,
        Dictionary<string, string> previousResults,
        DiscoveryTask task
    )
    {
        var substituted = new Dictionary<string, string>();

        foreach (var (key, value) in actionParameters)
        {
            var stringValue = value.ToString() ?? "";

            // Substitute references to previous action results
            foreach (var (actionId, result) in previousResults)
            {
                stringValue = stringValue.Replace($"${actionId}", result);
            }

            // Substitute task properties
            stringValue = stringValue.Replace("$source", task.Source);
            stringValue = stringValue.Replace("$keywords", string.Join(",", task.Keywords));
            stringValue = stringValue.Replace("$taskType", task.Type.ToString());

            substituted[key] = stringValue;
        }

        return substituted;
    }

    private static string GetEffectiveKeywords(
        Dictionary<string, string> parameters,
        DiscoveryTask task,
        string parameterName = "keywords"
    )
    {
        // First try to get keywords from parameters using the specified parameter name
        var keywords = parameters.GetValueOrDefault(parameterName, "");

        // If keywords are provided and not empty, use them
        if (!string.IsNullOrWhiteSpace(keywords))
        {
            return keywords;
        }

        // Try alternative parameter names
        if (parameterName != "keywords")
        {
            keywords = parameters.GetValueOrDefault("keywords", "");
            if (!string.IsNullOrWhiteSpace(keywords))
            {
                return keywords;
            }
        }

        // If task has keywords, use them
        if (task.Keywords.Any())
        {
            return string.Join(" ", task.Keywords);
        }

        // Fallback to paper metadata for targeted search
        var metadataKeywords = new List<string>();

        // Use DOI directly
        if (task.Metadata.TryGetValue("doi", out var doi) && !string.IsNullOrEmpty(doi))
        {
            metadataKeywords.Add(doi);
        }

        // Use paper title terms
        if (task.Metadata.TryGetValue("paper_title", out var title) && !string.IsNullOrEmpty(title))
        {
            metadataKeywords.Add(title);
        }

        // Use research domain
        if (
            task.Metadata.TryGetValue("research_domain", out var domain)
            && !string.IsNullOrEmpty(domain)
        )
        {
            metadataKeywords.Add(domain);
        }

        // If we have metadata keywords, use them
        if (metadataKeywords.Any())
        {
            return string.Join(" ", metadataKeywords);
        }

        // Last resort: use task type-based keywords
        return task.Type switch
        {
            DiscoveryTaskType.BugListDiscovery => "bug list defect dataset",
            DiscoveryTaskType.ArtifactRepositoryDiscovery => "artifact repository implementation",
            DiscoveryTaskType.VulnerabilityDiscovery => "vulnerability security bug",
            _ => "software engineering research",
        };
    }
}
