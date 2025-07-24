using System.Text.Json;
using System.Text.Json.Serialization;
using DataCollection.Application.Features.SemanticAgents.Models;
using Microsoft.SemanticKernel;

namespace DataCollection.Application.Serialization;

[JsonSerializable(typeof(DiscoveryTask))]
[JsonSerializable(typeof(DiscoveryResult))]
[JsonSerializable(typeof(DiscoveryResult[]))]
[JsonSerializable(typeof(List<DiscoveryResult>))]
[JsonSerializable(typeof(IEnumerable<DiscoveryResult>))]
[JsonSerializable(typeof(DiscoveryContext))]
[JsonSerializable(typeof(AgentPlan))]
[JsonSerializable(typeof(AgentAction))]
[JsonSerializable(typeof(AgentReflection))]
[JsonSerializable(typeof(FunctionResultContent))]
[JsonSerializable(typeof(UrlAnalysis))]
[JsonSerializable(typeof(UrlValidationResult))]
[JsonSerializable(typeof(List<UrlValidationResult>))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(LlmRepositoryVerificationResponse))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(List<Dictionary<string, object>>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(List<object>))]
[JsonSerializable(typeof(object))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
public partial class SemanticAgentsJsonContext : JsonSerializerContext;
