using System.Text.Json.Serialization;
using DataCollection.Application.Models.IssueTracker.Profiles;

namespace DataCollection.Application.Serialization;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower
)]
[JsonSerializable(typeof(UserProfile))]
public partial class ApplicationJsonContext : JsonSerializerContext;
