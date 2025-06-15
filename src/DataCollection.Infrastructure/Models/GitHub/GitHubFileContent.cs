namespace DataCollection.Infrastructure.Models.GitHub;

/// <summary>
/// GitHub file content response model
/// </summary>
public class GitHubFileContent
{
    public string? Content { get; set; }
    public string? Encoding { get; set; }
    public string? Name { get; set; }
    public string? Path { get; set; }
}
