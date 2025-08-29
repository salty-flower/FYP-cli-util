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
[JsonSerializable(typeof(Repository_merge_commit_message))]
[JsonSerializable(typeof(Repository_merge_commit_title))]
[JsonSerializable(typeof(Repository_squash_merge_commit_message))]
[JsonSerializable(typeof(Repository_squash_merge_commit_title))]
[JsonSerializable(typeof(NullableRepository_merge_commit_message))]
[JsonSerializable(typeof(NullableRepository_merge_commit_title))]
[JsonSerializable(typeof(NullableRepository_squash_merge_commit_message))]
[JsonSerializable(typeof(NullableRepository_squash_merge_commit_title))]
[JsonSerializable(typeof(Repository_merge_commit_message?))]
[JsonSerializable(typeof(Repository_merge_commit_title?))]
[JsonSerializable(typeof(Repository_squash_merge_commit_message?))]
[JsonSerializable(typeof(Repository_squash_merge_commit_title?))]
public partial class GitHubAPIJsonContext : JsonSerializerContext;
