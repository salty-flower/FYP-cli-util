using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using DataCollection.Core.Models;

namespace DataCollection.Application.Common.Services;

public partial class ValidationService
{
    private static readonly Regex DoiPattern = DoiRegex();

    public void ValidatePaper(Paper paper)
    {
        ArgumentNullException.ThrowIfNull(paper);

        if (string.IsNullOrWhiteSpace(paper.Doi))
            throw new ArgumentException("Paper DOI cannot be null or empty", nameof(paper));

        if (!DoiPattern.IsMatch(paper.Doi))
            throw new ArgumentException($"Invalid DOI format: {paper.Doi}", nameof(paper));

        if (string.IsNullOrWhiteSpace(paper.Title))
            throw new ArgumentException("Paper title cannot be null or empty", nameof(paper));
    }

    public void ValidateFilePath(string filePath, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath, paramName);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}", filePath);
    }

    public void ValidateUrl(string url, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url, paramName);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException($"Invalid URL format: {url}", paramName);

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException($"URL must use HTTP or HTTPS: {url}", paramName);
    }

    public void ValidateConfiguration<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T
    >(T config)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(config);

        var properties = typeof(T).GetProperties();
        foreach (var prop in properties)
        {
            var value = prop.GetValue(config);
            if (value == null && !prop.PropertyType.IsGenericType)
                throw new InvalidOperationException(
                    $"Configuration property {prop.Name} cannot be null"
                );
        }
    }

    public void ValidateRepositoryOwnerAndName(string owner, string repoName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner, nameof(owner));
        ArgumentException.ThrowIfNullOrWhiteSpace(repoName, nameof(repoName));

        if (owner.Length > 100)
            throw new ArgumentException("Repository owner name is too long", nameof(owner));

        if (repoName.Length > 100)
            throw new ArgumentException("Repository name is too long", nameof(repoName));
    }

    public void ValidateIssueCount(int issueCount)
    {
        if (issueCount < 0)
            throw new ArgumentException("Issue count cannot be negative", nameof(issueCount));
    }

    public void ValidateConfidenceScore(double confidence)
    {
        if (confidence < 0.0 || confidence > 1.0)
            throw new ArgumentException(
                "Confidence score must be between 0.0 and 1.0",
                nameof(confidence)
            );
    }

    [GeneratedRegex(@"^10\.\d{4,}\/[^\s]+$", RegexOptions.Compiled)]
    private static partial Regex DoiRegex();
}
