using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DataCollection.Infrastructure.Models.OpenAI;
using DataCollection.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using OpenAi.JsonSchema.Generator;
using OpenAi.JsonSchema.Serialization;

namespace DataCollection.Application.Features.IssueAnalysis.Rules;

public abstract class LargeLanguageModelCriterion<TProfile, TOutcome>(
    string model,
    ILogger<LargeLanguageModelCriterion<TProfile, TOutcome>> logger,
    OpenAIClient client,
    IHttpClientFactory httpClientFactory,
    BatchFileHandler batchFileHandler,
    BatchJobPoller batchJobPoller
) : IBatchCriterion<TProfile, TOutcome>
{
    protected readonly ILogger<LargeLanguageModelCriterion<TProfile, TOutcome>> Logger = logger;

    protected abstract IEnumerable<ChatMessage> BuildMessages(TProfile profile);

    protected virtual string OutcomeSchema =>
        new DefaultSchemaGenerator().Generate<TOutcome>(new JsonSchemaOptions()).ToJson();

    public string Model => model;

    protected OpenAIClient Client => client;

    protected abstract JsonTypeInfo<TOutcome> OutcomeJsonTypeInfo { get; }

    protected virtual TOutcome ParseOutcome(string llmResponse)
    {
        try
        {
            var outcome = JsonSerializer.Deserialize(llmResponse, OutcomeJsonTypeInfo);
            if (outcome == null)
                throw new InvalidOperationException(
                    $"Failed to deserialize LLM response to {typeof(TOutcome).Name}: {llmResponse}"
                );

            return outcome;
        }
        catch (JsonException ex)
        {
            Logger.LogError(ex, "Error parsing LLM response: {Response}", llmResponse);
            throw new InvalidOperationException(
                $"Failed to parse LLM response to {typeof(TOutcome).Name}",
                ex
            );
        }
    }

    public async Task<TOutcome> EvaluateAsync(TProfile profile)
    {
        var messages = BuildMessages(profile);

        try
        {
            Logger.LogTrace("Schema: {Schema}", OutcomeSchema);
            var options = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: $"{typeof(TProfile).Name}-{typeof(TOutcome).Name}-schema",
                    jsonSchema: BinaryData.FromString(OutcomeSchema),
                    jsonSchemaIsStrict: true
                ),
            };

            var chatClient = client.GetChatClient(model);
            var response = await chatClient.CompleteChatAsync(messages, options);

            if (response.Value.Content.Count == 0)
                throw new InvalidOperationException("No choices returned from LLM.");

            var result = response.Value.Content[0].Text;
            if (string.IsNullOrEmpty(result))
                throw new InvalidOperationException("Empty result from LLM.");
            return ParseOutcome(result);
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Error evaluating criterion for {ProfileType}",
                typeof(TProfile).Name
            );
            throw;
        }
    }

    public async Task<Dictionary<string, TOutcome>> EvaluateBatchAsync(
        Dictionary<string, TProfile> profiles,
        CancellationToken cancellationToken = default
    )
    {
        if (profiles.Count == 0)
        {
            Logger.LogWarning("No profiles provided for batch evaluation");
            return [];
        }

        Logger.LogInformation("Starting batch evaluation for {Count} profiles", profiles.Count);

        var batchRequests = CreateBatchRequests(profiles);
        var batchFileId = await batchFileHandler.UploadBatchFileAsync(
            batchRequests,
            cancellationToken
        );
        var batchJobId = await CreateBatchJobAsync(batchFileId, cancellationToken);
        var completedBatch = await batchJobPoller.PollBatchCompletionAsync(
            batchJobId,
            cancellationToken
        );
        var results = await batchJobPoller.DownloadAndParseResultsAsync(
            completedBatch,
            profiles.Keys,
            cancellationToken
        );

        Logger.LogInformation("Batch evaluation completed for {Count} profiles", results.Count);
        return results.ToDictionary(kvp => kvp.Key, kvp => ParseOutcome(kvp.Value));
    }

    public async Task<Dictionary<string, TOutcome>> ResumeBatchAsync(
        string batchJobId,
        CancellationToken cancellationToken = default
    )
    {
        Logger.LogInformation("Resuming batch job: {BatchJobId}", batchJobId);

        var completedBatch = await batchJobPoller.PollBatchCompletionAsync(
            batchJobId,
            cancellationToken
        );
        var results = await batchJobPoller.DownloadAndParseResultsAsync(
            completedBatch,
            [],
            cancellationToken
        );

        Logger.LogInformation(
            "Resume batch evaluation completed for {Count} profiles",
            results.Count
        );
        return results.ToDictionary(kvp => kvp.Key, kvp => ParseOutcome(kvp.Value));
    }

    private List<BatchRequest> CreateBatchRequests(Dictionary<string, TProfile> profiles) =>
        profiles
            .Select(p => new BatchRequest
            {
                CustomId = p.Key,
                Method = "POST",
                Url = "/v1/chat/completions",
                Body = new ChatCompletionRequest(
                    Model: model,
                    Messages: BuildMessages(p.Value)
                        .Select(m => new ChatMessageModel(
                            Role: m.GetType().Name.Replace("ChatMessage", "").ToLowerInvariant(),
                            Content: m switch
                            {
                                SystemChatMessage sys => sys.Content[0].Text,
                                UserChatMessage user => user.Content[0].Text,
                                _ => throw new InvalidOperationException(
                                    $"Unsupported message type: {m.GetType()}"
                                ),
                            }
                        ))
                        .ToArray(),
                    ResponseFormat: new ResponseFormatModel(
                        Type: "json_schema",
                        JsonSchema: new JsonSchemaModel(
                            Name: $"{typeof(TProfile).Name}-{typeof(TOutcome).Name}-schema",
                            Schema: new DefaultSchemaGenerator()
                                .Generate<TOutcome>(new JsonSchemaOptions())
                                .ToJsonNode()!,
                            Strict: true
                        )
                    )
                ),
            })
            .ToList();

    private async Task<string> CreateBatchJobAsync(
        string inputFileId,
        CancellationToken cancellationToken = default
    )
    {
        Logger.LogInformation("Creating batch job with input file: {FileId}", inputFileId);

        var httpClient = httpClientFactory.CreateClient("OpenAIBatchApi");

        var requestBody = new CreateBatchJobRequest(
            InputFileId: inputFileId,
            Endpoint: "/v1/chat/completions",
            CompletionWindow: "24h"
        );

        var json = JsonSerializer.Serialize(
            requestBody,
            OpenAIBatchRequestJsonContext.Default.CreateBatchJobRequest
        );
        var content = new StringContent(
            json,
            Encoding.UTF8,
            System.Net.Mime.MediaTypeNames.Application.Json
        );

        var response = await httpClient.PostAsync(
            "https://api.openai.com/v1/batches",
            content,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var batchResponse = JsonSerializer.Deserialize(
            responseContent,
            OpenAIBatchRequestJsonContext.Default.BatchJobResponse
        );

        Logger.LogInformation("Batch job created with ID: {BatchId}", batchResponse?.Id);
        return batchResponse?.Id
            ?? throw new InvalidOperationException("Failed to create batch job");
    }
}
