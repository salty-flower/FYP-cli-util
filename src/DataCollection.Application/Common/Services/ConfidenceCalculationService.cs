using DataCollection.Application.Features.BugDiscovery;
using DataCollection.Infrastructure.Models.BugList;

namespace DataCollection.Application.Common.Services;

public static class ConfidenceCalculationService
{
    private const double BugListConfidenceBoost = 0.1;
    private const double IssueCountConfidenceBoost = 0.05;
    private const double KeywordConfidenceBoost = 0.05;
    private const int LowIssueThreshold = 10;
    private const int MediumIssueThreshold = 50;
    private const int HighIssueThreshold = 100;

    public static double CalculateRepositoryConfidence(
        ArtifactRepository repository,
        BugListDiscoveryResult result
    )
    {
        var baseConfidence = repository.Confidence;

        // Boost confidence if we found bug lists in this repository
        var relatedBugLists = result.BugLists.Count(b =>
            b.Url.Contains(
                repository.Url.Replace("https://github.com/", ""),
                StringComparison.OrdinalIgnoreCase
            )
        );

        if (relatedBugLists > 0)
        {
            baseConfidence += BugListConfidenceBoost * relatedBugLists;
        }

        return Math.Min(1.0, baseConfidence);
    }

    public static double CalculateIssueConfidence(int issueCount)
    {
        var confidence = BugDiscoveryConstants.RepositoryAnalysisConfidence;

        // Boost confidence based on issue activity
        if (issueCount > LowIssueThreshold)
            confidence += IssueCountConfidenceBoost;
        if (issueCount > MediumIssueThreshold)
            confidence += IssueCountConfidenceBoost;
        if (issueCount > HighIssueThreshold)
            confidence += IssueCountConfidenceBoost;

        return Math.Min(1.0, confidence);
    }

    public static double CalculatePdfAnalysisConfidence(int issueCount, bool hasKeywords)
    {
        var confidence = BugDiscoveryConstants.PdfAnalysisConfidence;

        // Boost confidence based on issue count
        if (issueCount > LowIssueThreshold)
            confidence += IssueCountConfidenceBoost;
        if (issueCount > MediumIssueThreshold)
            confidence += IssueCountConfidenceBoost;

        // Boost confidence if contains specific keywords
        if (hasKeywords)
            confidence += KeywordConfidenceBoost;

        return Math.Min(1.0, confidence);
    }

    public static double CalculateKeywordAnalysisConfidence(
        string context,
        bool hasArtifactKeywords
    )
    {
        return hasArtifactKeywords
            ? BugDiscoveryConstants.KeywordAnalysisConfidence
            : BugDiscoveryConstants.PdfAnalysisConfidence;
    }
}
