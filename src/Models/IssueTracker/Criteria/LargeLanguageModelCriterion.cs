using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DataCollection.Models.OpenAI;
using DataCollection.Serialization;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Files;
using OpenAi.JsonSchema.Generator;
using OpenAi.JsonSchema.Serialization;

namespace DataCollection.Models.IssueTracker.Criteria;

public abstract class LargeLanguageModelCriterion<TProfile, TOutcome>(
    string model,
    ILogger<LargeLanguageModelCriterion<TProfile, TOutcome>> logger,
    OpenAIClient client,
    IHttpClientFactory httpClientFactory
) : IBatchCriterion<TProfile, TOutcome>
{
    protected readonly string Model = model;
    protected readonly OpenAIClient Client = client;
    private readonly OpenAIFileClient fileClient = client.GetOpenAIFileClient();

    protected abstract IEnumerable<ChatMessage> BuildMessages(TProfile profile);

    protected virtual string OutcomeSchema =>
        new DefaultSchemaGenerator().Generate<TOutcome>(new JsonSchemaOptions()).ToJson();

    protected virtual TOutcome ParseOutcome(string llmResponse)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var outcome = JsonSerializer.Deserialize<TOutcome>(llmResponse, options);
            if (outcome == null)
                throw new InvalidOperationException(
                    $"Failed to deserialize LLM response to {typeof(TOutcome).Name}: {llmResponse}"
                );

            return outcome;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Error parsing LLM response: {Response}", llmResponse);
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
            logger.LogTrace("Schema: {Schema}", OutcomeSchema);
            var options = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: $"{typeof(TProfile).Name}-{typeof(TOutcome).Name}-schema",
                    jsonSchema: BinaryData.FromString(OutcomeSchema),
                    jsonSchemaIsStrict: true
                ),
            };

            var chatClient = Client.GetChatClient(Model);
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
            logger.LogError(
                ex,
                "Error evaluating criterion for {ProfileType}",
                typeof(TProfile).Name
            );
            throw;
        }
    }

    public async Task<Dictionary<string, TOutcome>> EvaluateBatchAsync(
        Dictionary<string, TProfile> profiles
    )
    {
        if (profiles.Count == 0)
        {
            logger.LogWarning("No profiles provided for batch evaluation");
            return [];
        }

        logger.LogInformation("Starting batch evaluation for {Count} profiles", profiles.Count);

        // Create batch requests
        var batchRequests = CreateBatchRequests(profiles);

        // Upload batch file
        var batchFileId = await UploadBatchFileAsync(batchRequests);

        // Create and submit batch job using HTTP client directly since BatchClient is experimental
        var batchJobId = await CreateBatchJobAsync(batchFileId);

        // Poll for completion
        var completedBatch = await PollBatchCompletionAsync(batchJobId);

        // Download and parse results
        var results = await DownloadAndParseResultsAsync(completedBatch, profiles.Keys);

        logger.LogInformation("Batch evaluation completed for {Count} profiles", results.Count);
        return results;
    }

    public async Task<Dictionary<string, TOutcome>> ResumeBatchAsync(string batchJobId)
    {
        logger.LogInformation("Resuming batch job: {BatchJobId}", batchJobId);

        // Poll for completion
        var completedBatch = await PollBatchCompletionAsync(batchJobId);

        // Download and parse results - we don't have the original custom IDs, so we'll extract them from results
        var results = await DownloadAndParseResultsAsync(completedBatch, []);

        logger.LogInformation(
            "Resume batch evaluation completed for {Count} profiles",
            results.Count
        );
        return results;
    }

    private List<BatchRequest> CreateBatchRequests(Dictionary<string, TProfile> profiles)
    {
        var requests = new List<BatchRequest>();

        foreach (var (customId, profile) in profiles)
        {
            var messages = BuildMessages(profile);

            var chatMessages = messages
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
                .ToArray();

            var requestBody = new ChatCompletionRequest(
                Model: Model,
                Messages: chatMessages,
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
            );

            requests.Add(
                new BatchRequest
                {
                    CustomId = customId,
                    Method = "POST",
                    Url = "/v1/chat/completions",
                    Body = requestBody,
                }
            );
        }

        return requests;
    }

    private async Task<string> UploadBatchFileAsync(List<BatchRequest> requests)
    {
        logger.LogInformation("Uploading batch file with {Count} requests", requests.Count);

        // Create JSONL content
        var jsonlContent = new StringBuilder();
        foreach (var request in requests)
        {
            var batchRequestModel = new BatchRequestModel(
                CustomId: request.CustomId,
                Method: request.Method,
                Url: request.Url,
                Body: request.Body
            );

            var requestJson = JsonSerializer.Serialize(
                batchRequestModel,
                OpenAIBatchRequestJsonContext.Default.BatchRequestModel
            );
            jsonlContent.AppendLine(requestJson);
        }

        // Create temporary file
        var tempFilePath = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFilePath, jsonlContent.ToString());
        logger.LogDebug("Wrote batch requests to temporary file: {FilePath}", tempFilePath);

        try
        {
            // Upload file
            using var fileStream = File.OpenRead(tempFilePath);
            var uploadedFile = await fileClient.UploadFileAsync(
                fileStream,
                Path.GetFileName(tempFilePath),
                FileUploadPurpose.Batch
            );

            logger.LogInformation("Batch file uploaded with ID: {FileId}", uploadedFile.Value.Id);
            return uploadedFile.Value.Id;
        }
        finally
        {
            // Clean up temp file
            if (File.Exists(tempFilePath))
                File.Delete(tempFilePath);
        }
    }

    private async Task<string> CreateBatchJobAsync(string inputFileId)
    {
        logger.LogInformation("Creating batch job with input file: {FileId}", inputFileId);

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

        var response = await httpClient.PostAsync("https://api.openai.com/v1/batches", content);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        var batchResponse = JsonSerializer.Deserialize(
            responseContent,
            OpenAIBatchRequestJsonContext.Default.BatchJobResponse
        );

        logger.LogInformation("Batch job created with ID: {BatchId}", batchResponse?.Id);
        return batchResponse?.Id
            ?? throw new InvalidOperationException("Failed to create batch job");
    }

    private async Task<BatchJobResponse> PollBatchCompletionAsync(string batchId)
    {
        logger.LogInformation("Polling batch job {BatchId} for completion", batchId);

        var pollInterval = TimeSpan.FromSeconds(30);
        var maxWaitTime = TimeSpan.FromHours(25); // Slightly more than 24 hours
        var startTime = DateTime.UtcNow;

        var httpClient = httpClientFactory.CreateClient("OpenAIBatchApi");

        while (DateTime.UtcNow - startTime < maxWaitTime)
        {
            var response = await httpClient.GetAsync(
                $"https://api.openai.com/v1/batches/{batchId}"
            );
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var batchJobResponse = JsonSerializer.Deserialize(
                responseContent,
                OpenAIBatchRequestJsonContext.Default.BatchJobResponse
            );

            logger.LogDebug("Batch {BatchId} status: {Status}", batchId, batchJobResponse?.Status);
            logger.LogDebug("Batch response JSON: {ResponseContent}", responseContent);

            switch (batchJobResponse?.Status?.ToLowerInvariant())
            {
                case "completed":
                    logger.LogInformation("Batch {BatchId} completed successfully", batchId);
                    if (string.IsNullOrEmpty(batchJobResponse.OutputFileId))
                    {
                        logger.LogError(
                            "Batch {BatchId} completed but has no output_file_id. Full response: {Response}",
                            batchId,
                            responseContent
                        );
                    }
                    return batchJobResponse;

                case "failed":
                case "expired":
                case "cancelled":
                    throw new InvalidOperationException(
                        $"Batch {batchId} failed with status: {batchJobResponse.Status}"
                    );

                case "in_progress":
                case "finalizing":
                case "validating":
                    // Continue polling
                    await Task.Delay(pollInterval);
                    break;

                default:
                    logger.LogWarning("Unknown batch status: {Status}", batchJobResponse?.Status);
                    await Task.Delay(pollInterval);
                    break;
            }
        }

        throw new TimeoutException(
            $"Batch {batchId} did not complete within the maximum wait time"
        );
    }

    private async Task<Dictionary<string, TOutcome>> DownloadAndParseResultsAsync(
        BatchJobResponse completedBatch,
        IEnumerable<string> expectedCustomIds
    )
    {
        if (string.IsNullOrEmpty(completedBatch.OutputFileId))
        {
            logger.LogError(
                "Completed batch has no output file ID. Batch status: {Status}, Error file ID: {ErrorFileId}",
                completedBatch.Status,
                completedBatch.ErrorFileId
            );

            // If there's an error file, try to download and log it
            if (!string.IsNullOrEmpty(completedBatch.ErrorFileId))
            {
                try
                {
                    var errorContent = await fileClient.DownloadFileAsync(
                        completedBatch.ErrorFileId
                    );
                    logger.LogError(
                        "Batch error file content: {ErrorContent}",
                        errorContent.Value.ToString()
                    );
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to download error file {ErrorFileId}",
                        completedBatch.ErrorFileId
                    );
                }
            }

            throw new InvalidOperationException(
                $"Completed batch has no output file ID. Status: {completedBatch.Status}"
            );
        }

        logger.LogInformation(
            "Downloading batch results from file: {FileId}",
            completedBatch.OutputFileId
        );

        // Download the results file
        var fileContent = await fileClient.DownloadFileAsync(completedBatch.OutputFileId);

        var results = new Dictionary<string, TOutcome>();
        var expectedIds = new HashSet<string>(expectedCustomIds);

        // Parse JSONL results
        using var reader = new StringReader(fileContent.Value.ToString());
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var batchResponse = JsonSerializer.Deserialize(
                    line,
                    OpenAIBatchRequestJsonContext.Default.BatchResponse
                );
                if (batchResponse?.CustomId == null)
                {
                    logger.LogWarning("Batch response missing custom_id: {Line}", line);
                    continue;
                }

                if (expectedIds.Count > 0 && !expectedIds.Contains(batchResponse.CustomId))
                {
                    logger.LogWarning(
                        "Unexpected custom_id in batch response: {CustomId}",
                        batchResponse.CustomId
                    );
                    continue;
                }

                if (
                    batchResponse.Response?.Body?.Choices?.FirstOrDefault()?.Message?.Content
                    == null
                )
                {
                    logger.LogWarning(
                        "Batch response missing content for {CustomId}",
                        batchResponse.CustomId
                    );
                    continue;
                }

                var content = batchResponse.Response.Body.Choices.First().Message.Content;
                var analysisResponse = ParseOutcome(content);
                results[batchResponse.CustomId] = analysisResponse;

                logger.LogDebug("Parsed result for {CustomId}", batchResponse.CustomId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to parse batch response line: {Line}", line);
            }
        }

        // Check for missing results only if we have specific expected IDs
        if (expectedIds.Count > 0)
        {
            var missingIds = expectedIds.Except(results.Keys).ToList();
            if (missingIds.Count > 0)
            {
                logger.LogWarning(
                    "Missing results for {Count} custom IDs: {MissingIds}",
                    missingIds.Count,
                    string.Join(", ", missingIds)
                );
            }
        }

        logger.LogInformation(
            "Successfully parsed {Count} results out of {Expected} expected",
            results.Count,
            expectedIds.Count > 0 ? expectedIds.Count : results.Count // Adjust expected count when resuming
        );

        return results;
    }

    // Helper class for batch processing
    private class BatchRequest
    {
        public required string CustomId { get; set; }
        public required string Method { get; set; }
        public required string Url { get; set; }
        public required ChatCompletionRequest Body { get; set; }
    }
}
