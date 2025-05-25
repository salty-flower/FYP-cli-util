using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using DataCollection.Models.IssueTracker;
using DataCollection.Models.IssueTracker.Responses;
using DataCollection.Models.OpenAI;
using DataCollection.Options;
using EnumsNET;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Services;

public class IssueAnalysisStorageService(
    ILogger<IssueAnalysisStorageService> logger,
    IOptions<PathsOptions> pathsOptions
)
{
    public async Task SaveAnalysisResultAsync(
        string owner,
        string repoName,
        long issueNumber,
        IssueStatus status,
        IssueAnalysisResponse analysisResult
    )
    {
        var repoDir = Path.Combine(pathsOptions.Value.IssueAnalysisDir, owner, repoName);
        Directory.CreateDirectory(repoDir);

        var resultFileName = Path.Combine(repoDir, $"issue_{issueNumber}.json");

        var resultObject = new AnalysisResultModel(
            Owner: owner,
            Repository: repoName,
            IssueNumber: issueNumber,
            Status: status.ToString(),
            StatusDescription: status.AsString(EnumFormat.Description)!,
            Analysis: analysisResult
        );

        var json = JsonSerializer.Serialize(
            resultObject,
            IssueCacheJsonContext.Default.AnalysisResultModel
        );
        await File.WriteAllTextAsync(resultFileName, json);

        logger.LogInformation("Saved analysis result to {ResultFileName}", resultFileName);
    }

    /// <summary>
    /// Tries to get cached analysis result from disk
    /// </summary>
    public async Task<CachedAnalysisResult?> TryGetCachedAnalysisResultAsync(
        string owner,
        string repoName,
        long issueNumber
    )
    {
        var resultFile = Path.Combine(
            pathsOptions.Value.IssueAnalysisDir,
            owner,
            repoName,
            $"issue_{issueNumber}.json"
        );

        if (!File.Exists(resultFile))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(resultFile);
            return JsonSerializer.Deserialize(
                json,
                IssueCacheJsonContext.Default.CachedAnalysisResult
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to read cached analysis for {Owner}/{Repo}#{IssueNumber}",
                owner,
                repoName,
                issueNumber
            );
        }

        return null;
    }
}
