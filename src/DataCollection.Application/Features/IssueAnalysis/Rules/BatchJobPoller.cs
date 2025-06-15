using System.Text.Json;
using DataCollection.Infrastructure.Models.OpenAI;
using DataCollection.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace DataCollection.Application.Features.IssueAnalysis.Rules;

public class BatchJobPoller(
    ILogger<BatchJobPoller> logger,
    IHttpClientFactory httpClientFactory,
    OpenAIClient client
)
{
    public async Task<BatchJobResponse> PollBatchCompletionAsync(string batchId)
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

    public async Task<Dictionary<string, string>> DownloadAndParseResultsAsync(
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

            if (!string.IsNullOrEmpty(completedBatch.ErrorFileId))
            {
                try
                {
                    var errorContent = await client
                        .GetOpenAIFileClient()
                        .DownloadFileAsync(completedBatch.ErrorFileId);
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

        var fileContent = await client
            .GetOpenAIFileClient()
            .DownloadFileAsync(completedBatch.OutputFileId);

        var results = new Dictionary<string, string>();
        var expectedIds = new HashSet<string>(expectedCustomIds);

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

                var content =
                    batchResponse.Response?.Body?.Choices?.First()?.Message?.Content
                    ?? string.Empty;
                results[batchResponse.CustomId] = content;

                logger.LogDebug("Parsed result for {CustomId}", batchResponse.CustomId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to parse batch response line: {Line}", line);
            }
        }

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
            expectedIds.Count > 0 ? expectedIds.Count : results.Count
        );

        return results;
    }
}
