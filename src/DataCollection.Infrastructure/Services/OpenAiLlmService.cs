using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CSharpFunctionalExtensions;
using DataCollection.Core.Interfaces;
using DataCollection.Core.Models.Errors;
using DataCollection.Infrastructure.Models.OpenAI;
using DataCollection.Infrastructure.Options;
using DataCollection.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Files;
using OpenAi.JsonSchema.Generator;

namespace DataCollection.Infrastructure.Services;

public class OpenAiLlmService<TProfile, TOutcome> : ILargeLanguageModelService<TProfile, TOutcome>
    where TOutcome : class
{
    private readonly OpenAIClient _client;
    private readonly LlmOptions _options;
    private readonly HttpClient _httpClient;
    private readonly OpenAIFileClient _fileClient;
    private readonly ILogger<OpenAiLlmService<TProfile, TOutcome>> _logger;

    public OpenAiLlmService(
        OpenAIClient client,
        IHttpClientFactory httpClientFactory,
        IOptions<LlmOptions> options,
        ILogger<OpenAiLlmService<TProfile, TOutcome>> logger
    )
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("OpenAIBatchApi");
        _fileClient = client.GetOpenAIFileClient();
    }

    public async Task<Result<TOutcome, IssueAnalysisError>> EvaluateAsync(
        TProfile profile,
        string modelName
    )
    {
        var effectiveModelName = modelName ?? _options.DefaultModel;
        try
        {
            var schema = new DefaultSchemaGenerator().Generate<TOutcome>().ToJson();
            var options = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: $"{typeof(TProfile).Name}-{typeof(TOutcome).Name}-schema",
                    jsonSchema: BinaryData.FromString(schema),
                    jsonSchemaIsStrict: true
                ),
            };

            var messages = new ChatMessage[]
            {
                new SystemChatMessage(
                    "You are a helpful assistant that analyzes data and returns JSON."
                ),
                new UserChatMessage(
                    $"Analyze the following profile and return the outcome in the specified JSON format:\n{JsonSerializer.Serialize(profile)}"
                ),
            };

            var chatClient = _client.GetChatClient(effectiveModelName);
            var response = await chatClient.CompleteChatAsync(messages, options);

            if (response.Value.Content.Count == 0)
                return Result.Failure<TOutcome, IssueAnalysisError>(
                    new LlmAnalysisError(
                        effectiveModelName,
                        "The response from the model was empty."
                    )
                );

            var json = response.Value.Content[0].Text;
            var outcome = JsonSerializer.Deserialize<TOutcome>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            return outcome is not null
                ? Result.Success<TOutcome, IssueAnalysisError>(outcome)
                : Result.Failure<TOutcome, IssueAnalysisError>(
                    new LlmAnalysisError(
                        effectiveModelName,
                        "Failed to deserialize the model's response."
                    )
                );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "An unexpected error occurred during LLM evaluation for model {ModelName}.",
                effectiveModelName
            );
            return Result.Failure<TOutcome, IssueAnalysisError>(
                new LlmAnalysisError(
                    effectiveModelName,
                    $"An unexpected error occurred: {ex.Message}"
                )
            );
        }
    }

    public async Task<Result<Dictionary<string, TOutcome>, IssueAnalysisError>> EvaluateBatchAsync(
        Dictionary<string, TProfile> profiles,
        string modelName
    )
    {
        var effectiveModelName = modelName ?? _options.DefaultModel;
        try
        {
            var batchRequests = CreateBatchRequestModels(profiles, effectiveModelName);
            var tempFilePath = await WriteBatchRequestsToFileAsync(batchRequests);
            var fileId = await UploadBatchFileAsync(tempFilePath);
            var batchJobId = await CreateBatchJobAsync(fileId);
            return await PollForBatchCompletionAsync(batchJobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "An unexpected error occurred during batch LLM evaluation for model {ModelName}.",
                effectiveModelName
            );
            return Result.Failure<Dictionary<string, TOutcome>, IssueAnalysisError>(
                new LlmAnalysisError(
                    effectiveModelName,
                    $"An unexpected error occurred: {ex.Message}"
                )
            );
        }
    }

    public Task<Result<Dictionary<string, TOutcome>, IssueAnalysisError>> ResumeBatchAsync(
        string batchJobId
    )
    {
        return PollForBatchCompletionAsync(batchJobId);
    }

    private List<BatchRequestModel> CreateBatchRequestModels(
        IDictionary<string, TProfile> profiles,
        string modelName
    )
    {
        var schema = new DefaultSchemaGenerator()
            .Generate<TOutcome>(new JsonSchemaOptions())
            .ToJsonNode()!;

        return profiles
            .Select(p =>
            {
                var messages = new[]
                {
                    new ChatMessageModel(
                        "system",
                        "You are a helpful assistant that analyzes data and returns JSON."
                    ),
                    new ChatMessageModel(
                        "user",
                        $"Analyze the following profile and return the outcome in the specified JSON format:\n{JsonSerializer.Serialize(p.Value)}"
                    ),
                };

                var requestBody = new ChatCompletionRequest(
                    Model: modelName,
                    Messages: messages,
                    ResponseFormat: new ResponseFormatModel(
                        Type: "json_schema",
                        JsonSchema: new JsonSchemaModel(
                            Name: $"{typeof(TProfile).Name}-{typeof(TOutcome).Name}-schema",
                            Schema: schema,
                            Strict: true
                        )
                    )
                );
                return new BatchRequestModel(
                    p.Key,
                    "POST",
                    _options.BatchCompletionEndpoint,
                    requestBody
                );
            })
            .ToList();
    }

    private async Task<string> WriteBatchRequestsToFileAsync(
        IEnumerable<BatchRequestModel> requests
    )
    {
        var tempFilePath = Path.GetTempFileName();
        var jsonlContent = new StringBuilder();
        foreach (var request in requests)
        {
            jsonlContent.AppendLine(
                JsonSerializer.Serialize(
                    request,
                    OpenAIBatchRequestJsonContext.Default.BatchRequestModel
                )
            );
        }
        await File.WriteAllTextAsync(tempFilePath, jsonlContent.ToString());
        return tempFilePath;
    }

    private async Task<string> UploadBatchFileAsync(string filePath)
    {
        try
        {
            await using var fileStream = File.OpenRead(filePath);
            var uploadedFile = await _fileClient.UploadFileAsync(
                fileStream,
                Path.GetFileName(filePath),
                FileUploadPurpose.Batch
            );
            return uploadedFile.Value.Id;
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    private async Task<string> CreateBatchJobAsync(string inputFileId)
    {
        var requestBody = new CreateBatchJobRequest(
            inputFileId,
            _options.BatchCompletionEndpoint,
            _options.BatchCompletionWindow
        );
        var response = await _httpClient.PostAsJsonAsync("v1/batches", requestBody);
        response.EnsureSuccessStatusCode();
        var batchJob = await response.Content.ReadFromJsonAsync<BatchJobResponse>(
            OpenAIBatchRequestJsonContext.Default.BatchJobResponse
        );
        return batchJob!.Id!;
    }

    private async Task<
        Result<Dictionary<string, TOutcome>, IssueAnalysisError>
    > PollForBatchCompletionAsync(string batchId)
    {
        var timeout = DateTime.UtcNow.AddMinutes(_options.BatchPollingTimeoutMinutes);
        while (DateTime.UtcNow < timeout)
        {
            var response = await _httpClient.GetAsync($"v1/batches/{batchId}");
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning(
                    "Polling for batch {BatchId} failed with status {StatusCode} and content: {ErrorContent}",
                    batchId,
                    response.StatusCode,
                    errorContent
                );
                await Task.Delay(TimeSpan.FromSeconds(_options.BatchPollingIntervalSeconds));
                continue;
            }

            var batchJob = await response.Content.ReadFromJsonAsync<BatchJobResponse>(
                OpenAIBatchRequestJsonContext.Default.BatchJobResponse
            );

            switch (batchJob?.Status?.ToLowerInvariant())
            {
                case "completed":
                    return await ProcessCompletedBatchAsync(batchJob);
                case "failed":
                case "expired":
                case "cancelled":
                    var errorContent = await GetErrorFileContent(batchJob.ErrorFileId);
                    return Result.Failure<Dictionary<string, TOutcome>, IssueAnalysisError>(
                        new LlmAnalysisError(
                            "BatchProcessing",
                            $"Batch job finished with status: {batchJob.Status}. Errors: {errorContent}"
                        )
                    );
                default:
                    await Task.Delay(TimeSpan.FromSeconds(_options.BatchPollingIntervalSeconds));
                    break;
            }
        }

        return Result.Failure<Dictionary<string, TOutcome>, IssueAnalysisError>(
            new LlmAnalysisError("BatchProcessing", $"Batch job {batchId} timed out.")
        );
    }

    private async Task<string> GetErrorFileContent(string? errorFileId)
    {
        if (string.IsNullOrEmpty(errorFileId))
            return "No error file ID was provided.";
        try
        {
            var response = await _fileClient.DownloadFileAsync(errorFileId);
            if (response.GetRawResponse().IsError)
            {
                return $"Failed to download error file. Status: {response.GetRawResponse().Status}.";
            }
            return response.Value.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Exception while downloading error file {ErrorFileId}",
                errorFileId
            );
            return $"Failed to download error file content: {ex.Message}";
        }
    }

    private async Task<
        Result<Dictionary<string, TOutcome>, IssueAnalysisError>
    > ProcessCompletedBatchAsync(BatchJobResponse batchJob)
    {
        if (string.IsNullOrEmpty(batchJob.OutputFileId))
            return Result.Failure<Dictionary<string, TOutcome>, IssueAnalysisError>(
                new LlmAnalysisError(
                    "BatchProcessing",
                    "Batch job completed but output file ID is missing."
                )
            );

        var results = new Dictionary<string, TOutcome>();
        var responseContentResult = await _fileClient.DownloadFileAsync(batchJob.OutputFileId);

        var rawResponse = responseContentResult.GetRawResponse();
        if (rawResponse.IsError)
            return Result.Failure<Dictionary<string, TOutcome>, IssueAnalysisError>(
                new LlmAnalysisError(
                    "BatchProcessing",
                    $"Failed to download result file. Status: {rawResponse.Status}"
                )
            );

        using var reader = new StringReader(responseContentResult.Value.ToString());

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var batchResponse = JsonSerializer.Deserialize<BatchResponse>(
                line,
                OpenAIBatchRequestJsonContext.Default.BatchResponse
            );
            if (
                batchResponse?.CustomId != null
                && batchResponse.Response?.Body?.Choices?.FirstOrDefault()?.Message?.Content != null
            )
            {
                var content = batchResponse.Response.Body.Choices[0].Message.Content;
                var outcome = JsonSerializer.Deserialize<TOutcome>(
                    content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
                if (outcome != null)
                {
                    results[batchResponse.CustomId] = outcome;
                }
            }
        }

        return Result.Success<Dictionary<string, TOutcome>, IssueAnalysisError>(results);
    }
}
