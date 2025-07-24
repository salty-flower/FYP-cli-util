using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DataCollection.Application.Features.SemanticAgents.Models;
using DataCollection.Application.Features.SemanticAgents.Tools;
using DataCollection.Application.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

namespace DataCollection.Application.Features.SemanticAgents;

public interface IDiscoveryAgent
{
    [RequiresUnreferencedCode(
        "Calls ExecuteActionAsync which calls methods that require unreferenced code."
    )]
    [RequiresDynamicCode("Calls ExecuteActionAsync which calls methods that require dynamic code.")]
    Task<List<DiscoveryResult>> DiscoverAsync(DiscoveryTask task);
}

public class DiscoveryAgent(Kernel kernel, ILogger<DiscoveryAgent> logger) : IDiscoveryAgent
{
    [RequiresUnreferencedCode(
        "Calls ExecuteActionAsync which calls methods that require unreferenced code."
    )]
    [RequiresDynamicCode("Calls ExecuteActionAsync which calls methods that require dynamic code.")]
    public async Task<List<DiscoveryResult>> DiscoverAsync(DiscoveryTask task)
    {
        logger.LogInformation(
            "Starting discovery for task {TaskId}: {Description}",
            task.Id,
            task.Description
        );

        var allResults = new List<FunctionResultContent>();

        // Create comprehensive discovery prompt
        var discoveryPrompt = $"""
                You are an autonomous research agent specialized in discovering bug lists, artifact repositories, and vulnerabilities from academic papers and documentation.

                Execute the discovery task systematically using the available tools. Here are the task details:

                TASK DETAILS:
                - Type: {task.Type}
                - Description: {task.Description}
                - Source Content: {task.Source}
                - Keywords: {string.Join(", ", task.Keywords)}
                - Paper Metadata: {JsonSerializer.Serialize(
                    task.Metadata,
                    SemanticAgentsJsonContext.Default.DictionaryStringString
                )}

                DISCOVERY STRATEGY:
                Please systematically execute your discovery approach:
                1. Start by analyzing the source content for patterns and URLs using SearchTextPatterns
                2. Extract URLs from the content using ExtractUrls
                3. Use external searches (GitHub, web, Zenodo) with paper metadata when available
                4. Validate and analyze any discovered URLs using ValidateUrls
                5. Score and rank all results using ScoreResults

                INSTRUCTIONS:
                - Be thorough and systematic in your approach
                - Use multiple search strategies to ensure comprehensive coverage
                - Focus on finding high-quality, relevant results
                - Validate URLs to ensure they are accessible
                - Score results based on relevance and confidence

                Use the available tools to perform the discovery. Call the appropriate functions to gather comprehensive results.
                """;

        logger.LogInformation("Starting discovery execution for task {TaskId}", task.Id);

        // Use the pre-configured kernel that already has DiscoveryTools plugin loaded
        var agentKernel = kernel;
        logger.LogInformation(
            "Using pre-configured kernel with DiscoveryTools plugin for task {TaskId}",
            task.Id
        );

        var agent = new ChatCompletionAgent
        {
            Name = "DiscoveryAgent",
            Description =
                "Agent for discovering bug lists, artifact repositories, and vulnerabilities",
            Kernel = agentKernel,
            Instructions = discoveryPrompt,
            Arguments = new(
                new PromptExecutionSettings
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
                }
            ),
        };
        logger.LogInformation("Constructed agent for task {TaskId}", task.Id);
        await foreach (var i in agent.InvokeStreamingAsync("Please proceed to analyze the paper."))
            if (!string.IsNullOrWhiteSpace(i.Message.Content))
                logger.LogInformation("Agent response: {Response}", i.Message.Content);

        // TODO: find how to properly return a value
        return [];
    }
}
