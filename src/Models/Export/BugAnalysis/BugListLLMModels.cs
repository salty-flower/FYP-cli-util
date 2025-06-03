using System.Collections.Generic;

namespace DataCollection.Models.Export.BugAnalysis;

public record BugListAnalysisRequest(string PaperTitle, string PaperText);

public record BugListAnalysisResponse(List<ArtifactMention> Mentions);

public record ArtifactMention(
    string Type,
    string Description,
    string ProjectName,
    double Confidence
);

public record RepositoryVerificationRequest(
    string PaperTitle,
    string PaperAuthors,
    string RepositoryName,
    string RepositoryDescription,
    string ReadmeContent
);

public record RepositoryVerificationResponse(
    bool IsArtifactRepository,
    double Confidence,
    string Reasoning
);

public record ArtifactVerificationRequest(
    string PaperTitle,
    string PaperAuthors,
    string Url,
    string Context
);

public record ArtifactVerificationResponse(bool IsArtifact, double Confidence, string Reasoning);

public record RepositoryBugFileAnalysisRequest(List<string> FilePaths);

public record RepositoryBugFileAnalysisResponse(List<BugRelatedFile> BugRelatedFiles);

public record BugRelatedFile(string Path, string Type, string Description, double Confidence);
