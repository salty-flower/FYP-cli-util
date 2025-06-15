using DataCollection.Core.Models;

namespace DataCollection.Application.Common.Services;

public class LlmPromptService
{
    public string CreateRepositoryVerificationPrompt(
        Paper paper,
        string readmeContent,
        string repositoryUrl
    )
    {
        return $@"Analyze if this repository is related to the research paper.

Paper: {paper.Title}
DOI: {paper.Doi}
Repository: {repositoryUrl}

README excerpt:
{readmeContent}

Does this repository contain the implementation or artifacts mentioned in the paper?
Respond with only: YES or NO";
    }

    public string CreateBugListAnalysisPrompt(Paper paper, string content)
    {
        return $@"Analyze this content for bug tracking systems or issue collections.

Paper: {paper.Title}
Content: {content}

Identify:
1. Bug tracking systems mentioned
2. Issue tracker URLs
3. Defect databases or collections

Provide structured findings with URLs and descriptions.";
    }

    public string CreateArtifactVerificationPrompt(Paper paper, string url, string context)
    {
        return $@"Given this research paper context and URL, determine if the URL is likely an artifact repository for this paper.

Paper: {paper.Title}
Authors: {string.Join(", ", paper.Authors ?? [])}
Context: {context}
URL: {url}

Respond with only: YES or NO";
    }

    public string CreateProjectVerificationPrompt(
        Paper paper,
        string projectName,
        string projectContext,
        string repositoryUrl
    ) =>
        $@"Is this repository likely the implementation for the mentioned project?

Paper: {paper.Title}
Authors: {string.Join(", ", paper.Authors ?? [])}
Project mentioned: {projectName}
Project context: {projectContext}
Repository URL: {repositoryUrl}

Respond with only: YES or NO";

    public string CreateRepositoryRelevancePrompt(Paper paper, string repositoryUrl) =>
        $@"Is this repository likely an artifact/implementation for this research paper?

Paper: {paper.Title}
Authors: {string.Join(", ", paper.Authors ?? [])}
Repository: {repositoryUrl}

Respond with only: YES or NO";

    public string CreatePdfAnalysisPrompt(Paper paper, string text) =>
        $@"Analyze this research paper text and identify:
1. Bug tracking systems or issue trackers mentioned
2. Source code repositories or artifacts
3. Any references to bug lists, defect databases, or issue collections

Paper title: {paper.Title}
Text: {text}

Provide URLs and brief descriptions for any findings.";

    public string CreateDetailedRepositoryVerificationPrompt(
        Paper paper,
        string readmeContent,
        string repositoryUrl
    ) =>
        $@"Given the following research paper and repository details, assess if the repository is the official artifact or implementation for the paper.

Paper Title: {paper.Title}
Paper Authors: {string.Join(", ", paper.Authors ?? [])}
Paper Abstract: {paper.Abstract}

Repository URL: {repositoryUrl}
Repository README:
---
{readmeContent}
---

Analyze the connection and provide a structured JSON response with your assessment, including relevance, a confidence score, and a brief justification.";
}
