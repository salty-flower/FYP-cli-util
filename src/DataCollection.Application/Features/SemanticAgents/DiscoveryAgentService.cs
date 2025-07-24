using System.Diagnostics.CodeAnalysis;
using DataCollection.Application.Features.SemanticAgents.Models;
using Microsoft.Extensions.Logging;

namespace DataCollection.Application.Features.SemanticAgents;

public interface IDiscoveryAgentService
{
    Task<List<DiscoveryResult>> DiscoverBugListsAsync(
        string content,
        List<string>? keywords = null,
        Dictionary<string, string>? paperMetadata = null
    );
    Task<List<DiscoveryResult>> DiscoverArtifactRepositoriesAsync(
        string content,
        List<string>? keywords = null,
        Dictionary<string, string>? paperMetadata = null
    );
    Task<List<DiscoveryResult>> DiscoverVulnerabilitiesAsync(
        string content,
        List<string>? keywords = null,
        Dictionary<string, string>? paperMetadata = null
    );
    Task<List<DiscoveryResult>> DiscoverFromPdfAsync(
        string pdfPath,
        DiscoveryTaskType taskType,
        List<string>? keywords = null,
        Dictionary<string, string>? paperMetadata = null
    );
}

[RequiresUnreferencedCode(
    "Calls ExecuteActionAsync which calls methods that require unreferenced code."
)]
[RequiresDynamicCode("Calls ExecuteActionAsync which calls methods that require dynamic code.")]
public class DiscoveryAgentService(
    IDiscoveryAgent discoveryAgent,
    ILogger<DiscoveryAgentService> logger
) : IDiscoveryAgentService
{
    public async Task<List<DiscoveryResult>> DiscoverBugListsAsync(
        string content,
        List<string>? keywords = null,
        Dictionary<string, string>? paperMetadata = null
    )
    {
        var task = CreateDiscoveryTask(
            DiscoveryTaskType.BugListDiscovery,
            content,
            keywords,
            paperMetadata
        );
        logger.LogInformation(
            "Starting bug list discovery for content of length {Length}",
            content.Length
        );

        return await discoveryAgent.DiscoverAsync(task);
    }

    public async Task<List<DiscoveryResult>> DiscoverArtifactRepositoriesAsync(
        string content,
        List<string>? keywords = null,
        Dictionary<string, string>? paperMetadata = null
    )
    {
        var task = CreateDiscoveryTask(
            DiscoveryTaskType.ArtifactRepositoryDiscovery,
            content,
            keywords,
            paperMetadata
        );
        logger.LogInformation(
            "Starting artifact repository discovery for content of length {Length}",
            content.Length
        );

        return await discoveryAgent.DiscoverAsync(task);
    }

    public async Task<List<DiscoveryResult>> DiscoverVulnerabilitiesAsync(
        string content,
        List<string>? keywords = null,
        Dictionary<string, string>? paperMetadata = null
    )
    {
        var task = CreateDiscoveryTask(
            DiscoveryTaskType.VulnerabilityDiscovery,
            content,
            keywords,
            paperMetadata
        );
        logger.LogInformation(
            "Starting vulnerability discovery for content of length {Length}",
            content.Length
        );

        return await discoveryAgent.DiscoverAsync(task);
    }

    public async Task<List<DiscoveryResult>> DiscoverFromPdfAsync(
        string pdfPath,
        DiscoveryTaskType taskType,
        List<string>? keywords = null,
        Dictionary<string, string>? paperMetadata = null
    )
    {
        if (!File.Exists(pdfPath))
        {
            logger.LogError("PDF file not found: {PdfPath}", pdfPath);
            return [];
        }

        // Note: In a real implementation, you'd use a PDF text extraction library here
        // For now, we'll assume the content is provided or extracted elsewhere
        var metadata = new Dictionary<string, string>
        {
            ["source_type"] = "pdf",
            ["file_path"] = pdfPath,
            ["file_name"] = Path.GetFileName(pdfPath),
        };

        // Merge paper metadata if provided
        if (paperMetadata != null)
        {
            foreach (var kvp in paperMetadata)
            {
                metadata[kvp.Key] = kvp.Value;
            }
        }

        var task = new DiscoveryTask
        {
            Id = Guid.NewGuid().ToString(),
            Description = $"Discover {taskType} from PDF: {Path.GetFileName(pdfPath)}",
            Type = taskType,
            Source = pdfPath,
            Keywords = keywords ?? [],
            Metadata = metadata,
        };

        logger.LogInformation(
            "Starting {TaskType} discovery from PDF: {PdfPath}",
            taskType,
            pdfPath
        );

        return await discoveryAgent.DiscoverAsync(task);
    }

    private static DiscoveryTask CreateDiscoveryTask(
        DiscoveryTaskType type,
        string content,
        List<string>? keywords,
        Dictionary<string, string>? paperMetadata = null
    )
    {
        var metadata = new Dictionary<string, string>
        {
            ["source_type"] = "text",
            ["content_length"] = content.Length.ToString(),
        };

        // Merge paper metadata if provided
        if (paperMetadata != null)
        {
            foreach (var kvp in paperMetadata)
            {
                metadata[kvp.Key] = kvp.Value;
            }
        }

        return new DiscoveryTask
        {
            Id = Guid.NewGuid().ToString(),
            Description = $"Discover {type} from text content",
            Type = type,
            Source = content,
            Keywords = keywords ?? [],
            Metadata = metadata,
        };
    }
}
