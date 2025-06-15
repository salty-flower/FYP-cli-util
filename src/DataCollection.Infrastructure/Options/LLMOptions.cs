using System.ComponentModel.DataAnnotations;

namespace DataCollection.Infrastructure.Options;

public class LlmOptions
{
    public const string SectionName = "Llm";

    [Required]
    public string DefaultModel { get; set; } = "gpt-4o";

    [Required]
    public string BatchCompletionEndpoint { get; set; } = "/v1/chat/completions";

    [Required]
    public string BatchCompletionWindow { get; set; } = "24h";

    [Range(1, 600)]
    public int BatchPollingIntervalSeconds { get; set; } = 30;

    [Range(1, 300)]
    public int BatchPollingTimeoutMinutes { get; set; } = 10;
}
