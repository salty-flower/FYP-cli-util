using System.Text.Json.Serialization;
using DataCollection.Models.GitHub;

namespace DataCollection.Serialization;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(GitHubUser))]
[JsonSerializable(typeof(GitHubLabel))]
[JsonSerializable(typeof(GitHubMilestone))]
[JsonSerializable(typeof(GitHubRename))]
[JsonSerializable(typeof(GitHubDismissedReview))]
[JsonSerializable(typeof(GitHubEvent))]
[JsonSerializable(typeof(GitHubEventDetails))]
public partial class GitHubAPIJsonContext : JsonSerializerContext { }
