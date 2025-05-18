using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using OpenAi.JsonSchema.Generator;
using OpenAi.JsonSchema.Serialization;

namespace DataCollection.Models.IssueTracker;

public abstract class LargeLanguageModelCriterion<TProfile, TOutcome>(
    string model,
    ILogger logger,
    OpenAIClient client
) : ICriterion<TProfile, TOutcome>
{
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
                    $"Failed to deserialize LLM response to {typeof(TOutcome).Name}"
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
            logger.LogDebug("Schema: {Schema}", OutcomeSchema);
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

            logger.LogDebug("Received LLM response: {Response}", result);

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
}
