using System.Text.Json.Serialization;
using DataCollection.Application.Models.IssueTracker.Profiles;

namespace DataCollection.Application.Serialization;

[JsonSerializable(typeof(OtherEventProfile))]
[JsonSerializable(typeof(OtherEventProfile[]))]
[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
public partial class PromptSynthesizingJsonContext : JsonSerializerContext;
