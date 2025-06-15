namespace DataCollection.Infrastructure.Options;

public class CredentialOptions
{
    public required string GitHubToken { get; init; } = string.Empty;
    public required string OpenAIToken { get; init; } = string.Empty;
    public string OpenAIBaseUrl { get; init; } = "https://api.openai.com/v1";
}
