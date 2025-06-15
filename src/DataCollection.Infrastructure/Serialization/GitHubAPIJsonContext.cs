using System.Text.Json.Serialization;
using DataCollection.Infrastructure.Models.GitHub;
using GitHub.Models;
using GitHub.Users.Item;

namespace DataCollection.Infrastructure.Serialization;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(GitHubUser))]
[JsonSerializable(typeof(GitHubLabel))]
[JsonSerializable(typeof(GitHubMilestone))]
[JsonSerializable(typeof(GitHubRename))]
[JsonSerializable(typeof(GitHubDismissedReview))]
[JsonSerializable(typeof(GitHubEvent))]
[JsonSerializable(typeof(GitHubEventDetails))]
[JsonSerializable(typeof(FullRepository))]
[JsonSerializable(typeof(WithUsernameItemRequestBuilder.WithUsernameGetResponse))]
[JsonSerializable(typeof(RepositoryTree))]
[JsonSerializable(typeof(RepositoryTree.TreeItem))]
[JsonSerializable(typeof(GitHubSearchResponse))]
[JsonSerializable(typeof(GitHubFileContent))]
public partial class GitHubAPIJsonContext : JsonSerializerContext;
