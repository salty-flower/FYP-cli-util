namespace DataCollection.Core.Options;

public partial class CredentialOptions
{
    public required string GitHubToken { get; set; } = string.Empty;
    public required string OpenAIToken { get; set; } = string.Empty;
    public string OpenAIBaseUrl { get; set; } = "https://api.openai.com/v1";
    public string BigModel { get; set; } = "gpt-4.1";
    public string SmallModel { get; set; } = "o4-mini";
}
