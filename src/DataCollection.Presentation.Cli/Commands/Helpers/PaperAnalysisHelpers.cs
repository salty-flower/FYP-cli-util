using DataCollection.Application.Features.SemanticAgents;
using DataCollection.Application.Features.SemanticAgents.Models;

namespace DataCollection.Presentation.Cli.Commands.Helpers;

public static class PaperAnalysisHelpers
{
    public static async Task<List<DiscoveryResult>> AnalyzePaperContent(
        IDiscoveryAgentService agentService,
        string doi,
        string content,
        Dictionary<string, string> paperMetadata
    )
    {
        var results = await agentService.DiscoverBugListsAsync(content, null, paperMetadata);
        foreach (var result in results)
        {
            result.Metadata["source_location"] = "paper_content";
            result.Metadata["discovery_method"] = "direct_text_analysis";
        }
        return results;
    }

    public static async Task<List<DiscoveryResult>> DiscoverArtifacts(
        IDiscoveryAgentService agentService,
        string content,
        Dictionary<string, string> paperMetadata
    )
    {
        var results = await agentService.DiscoverArtifactRepositoriesAsync(
            content,
            null,
            paperMetadata
        );
        foreach (var result in results)
        {
            result.Metadata["source_location"] = "paper_content";
            result.Metadata["discovery_method"] = "paper_text_analysis";
        }
        return results;
    }

    public static async Task<List<DiscoveryResult>> SearchExternalPlatforms(
        IDiscoveryAgentService agentService,
        string searchTerms,
        Dictionary<string, string> paperMetadata
    )
    {
        var results = await agentService.DiscoverArtifactRepositoriesAsync(
            searchTerms,
            ["github", "zenodo", "figshare", "artifact"],
            paperMetadata
        );
        foreach (var result in results)
        {
            result.Metadata["source_location"] = "external_platform";
            result.Metadata["discovery_method"] = "platform_search";
        }
        return results;
    }

    public static async Task<List<DiscoveryResult>> AnalyzeArtifacts(
        IDiscoveryAgentService agentService,
        List<DiscoveryResult> artifacts,
        Dictionary<string, string> paperMetadata
    )
    {
        var bugResults = new List<DiscoveryResult>();
        foreach (var artifact in artifacts)
        {
            var repoContent = $"Repository content analysis for: {artifact.Title}";
            var repoBugResults = await agentService.DiscoverBugListsAsync(
                repoContent,
                ["bug", "issue", "defect", "failure", "error"],
                paperMetadata
            );

            foreach (var result in repoBugResults)
            {
                result.Metadata["source_location"] = "artifact_repository";
                result.Metadata["repository_url"] = artifact.Url;
                result.Metadata["repository_source"] = artifact.Metadata.TryGetValue(
                    "source_location",
                    out var repoSource
                )
                    ? repoSource
                    : "unknown";
                result.Metadata["discovery_method"] = "repository_file_analysis";
            }
            bugResults.AddRange(repoBugResults);
        }
        return bugResults;
    }
}
