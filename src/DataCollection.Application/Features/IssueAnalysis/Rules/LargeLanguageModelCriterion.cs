using System.Text;
using System.Text.Json;
using DataCollection.Infrastructure.Models.OpenAI;
using DataCollection.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Files;
using OpenAi.JsonSchema.Generator;
using OpenAi.JsonSchema.Serialization;
using DataCollection.Core.Interfaces;

namespace DataCollection.Application.Features.IssueAnalysis.Rules;

public abstract class LargeLanguageModelCriterion<TProfile, TOutcome>
    : IBatchCriterion<TProfile, TOutcome>
    where TOutcome : class, new()
{
    private readonly string _model;
    private readonly ILargeLanguageModelService<TProfile, TOutcome> _llmService;
    private readonly ILogger<LargeLanguageModelCriterion<TProfile, TOutcome>> _logger;

    protected LargeLanguageModelCriterion(
        string model,
        ILargeLanguageModelService<TProfile, TOutcome> llmService,
        ILogger<LargeLanguageModelCriterion<TProfile, TOutcome>> logger
    )
    {
        _model = model;
        _llmService = llmService;
        _logger = logger;
    }

    public async Task<TOutcome> EvaluateAsync(TProfile profile)
    {
        var result = await _llmService.EvaluateAsync(profile, _model);

        return result.Match(
            onSuccess: outcome => outcome,
            onFailure: error =>
            {
                _logger.LogError(
                    "LLM evaluation failed: {ErrorCode} - {ErrorMessage}",
                    error.Code,
                    error.Message
                );
                // Depending on desired behavior, could return default or throw.
                // Throwing seems more appropriate as it indicates a failure in a sub-process.
                throw new InvalidOperationException($"LLM evaluation failed: {error.Message}");
            }
        );
    }

    public async Task<Dictionary<string, TOutcome>> EvaluateBatchAsync(
        Dictionary<string, TProfile> profiles
    )
    {
        var result = await _llmService.EvaluateBatchAsync(profiles, _model);

        return result.Match(
            onSuccess: outcomes => outcomes,
            onFailure: error =>
            {
                _logger.LogError(
                    "LLM batch evaluation failed: {ErrorCode} - {ErrorMessage}",
                    error.Code,
                    error.Message
                );
                throw new InvalidOperationException(
                    $"LLM batch evaluation failed: {error.Message}"
                );
            }
        );
    }

    public async Task<Dictionary<string, TOutcome>> ResumeBatchAsync(string batchJobId)
    {
        var result = await _llmService.ResumeBatchAsync(batchJobId);

        return result.Match(
            onSuccess: outcomes => outcomes,
            onFailure: error =>
            {
                _logger.LogError(
                    "LLM batch resume failed: {ErrorCode} - {ErrorMessage}",
                    error.Code,
                    error.Message
                );
                throw new InvalidOperationException($"LLM batch resume failed: {error.Message}");
            }
        );
    }
}
