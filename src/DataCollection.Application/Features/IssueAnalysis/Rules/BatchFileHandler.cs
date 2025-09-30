using System.Text.Json;
using DataCollection.Common.Extensions;
using DataCollection.Infrastructure.Models.OpenAI;
using DataCollection.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Files;

namespace DataCollection.Application.Features.IssueAnalysis.Rules;

public class BatchFileHandler(ILogger<BatchFileHandler> logger, OpenAIClient client)
{
    public async Task<string> UploadBatchFileAsync(
        List<BatchRequest> requests,
        CancellationToken cancellationToken = default
    )
    {
        logger.LogInformation("Uploading batch file with {Count} requests", requests.Count);

        var tempFilePath = Path.GetTempFileName();
        await File.WriteAllTextAsync(
            tempFilePath,
            '\n'.Join(
                requests
                    .Select(request => new BatchRequestModel(
                        CustomId: request.CustomId,
                        Method: request.Method,
                        Url: request.Url,
                        Body: request.Body
                    ))
                    .Select(batchRequestModel =>
                        JsonSerializer.Serialize(
                            batchRequestModel,
                            OpenAIBatchRequestJsonContext.Default.BatchRequestModel
                        )
                    )
            ),
            cancellationToken
        );
        logger.LogDebug("Wrote batch requests to temporary file: {FilePath}", tempFilePath);

        try
        {
            await using var fileStream = File.OpenRead(tempFilePath);
            var uploadedFile = await client
                .GetOpenAIFileClient()
                .UploadFileAsync(
                    fileStream,
                    Path.GetFileName(tempFilePath),
                    FileUploadPurpose.Batch,
                    cancellationToken
                );

            logger.LogInformation("Batch file uploaded with ID: {FileId}", uploadedFile.Value.Id);
            return uploadedFile.Value.Id;
        }
        finally
        {
            if (File.Exists(tempFilePath))
                File.Delete(tempFilePath);
        }
    }
}
