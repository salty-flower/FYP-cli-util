using DataCollection.Models.GitHub;
using DataCollection.Models.IssueTracker.Profiles;
using GitHub.Models;
using GitHub.Users.Item;
using System.Text.Json.Serialization;

namespace DataCollection.Serialization;

[JsonSourceGenerationOptions(WriteIndented = false, PropertyNameCaseInsensitive =true, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
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
[JsonSerializable(typeof(UserProfile))]
public partial class GitHubAPIJsonContext : JsonSerializerContext { }
