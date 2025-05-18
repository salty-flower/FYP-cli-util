namespace DataCollection.Options;

public partial class CredentialOptions
{
    public required string GitHubToken { get; set; } = string.Empty;
    public required string OpenAIToken { get; set; } = string.Empty;
}
