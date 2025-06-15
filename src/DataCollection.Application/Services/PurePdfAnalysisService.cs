using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;
using DataCollection.Application.Features.PdfAnalysis;
using DataCollection.Application.Models.Commands;
using DataCollection.Core.Models.Errors;
using DataCollection.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Application.Services;

public class PurePdfAnalysisService : IPdfAnalysisService
{
    private readonly ILogger<PurePdfAnalysisService> logger;
    private readonly PdfAnalysisOptions options;

    public PurePdfAnalysisService(
        ILogger<PurePdfAnalysisService> logger,
        IOptions<PdfAnalysisOptions> options
    )
    {
        this.logger = logger;
        this.options = options.Value;
    }

    public async Task<Result<List<string>, BugDiscoveryError>> ReconstructParagraphsAsync(
        ReconstructParagraphsCommand command,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            logger.LogDebug(
                "Reconstructing paragraphs from {LineCount} lines",
                command.PageLines.Length
            );

            if (command.PageLines == null || command.PageLines.Length == 0)
                return Result.Success<List<string>, BugDiscoveryError>(new List<string>());

            var paragraphs = PdfTextUtils.ReconstructParagraphs(command.PageLines);

            logger.LogDebug(
                "Successfully reconstructed {ParagraphCount} paragraphs",
                paragraphs.Count
            );

            return Result.Success<List<string>, BugDiscoveryError>(paragraphs);
        }
        catch (OperationCanceledException)
        {
            return new PdfAnalysisError("", "Operation was cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reconstructing paragraphs");
            return new PdfAnalysisError("", ex.Message);
        }
    }

    public async Task<Result<Dictionary<string, int>, BugDiscoveryError>> CountKeywordsAsync(
        CountKeywordsCommand command,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            logger.LogDebug(
                "Counting {KeywordCount} keywords in text of length {TextLength}",
                command.Keywords.Length,
                command.Text.Length
            );

            if (
                string.IsNullOrWhiteSpace(command.Text)
                || command.Keywords == null
                || !command.Keywords.Any()
            )
                return Result.Success<Dictionary<string, int>, BugDiscoveryError>(
                    new Dictionary<string, int>()
                );

            var keywords = command.NormalizeKeywords
                ? command.Keywords.Select(k => k.ToLowerInvariant()).ToArray()
                : command.Keywords;

            var result = PaperAnalyzer.CountKeywordsInText(command.Text, keywords);

            logger.LogDebug(
                "Found {TotalOccurrences} total keyword occurrences",
                result.Values.Sum()
            );

            return Result.Success<Dictionary<string, int>, BugDiscoveryError>(result);
        }
        catch (OperationCanceledException)
        {
            return new PdfAnalysisError("", "Operation was cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error counting keywords");
            return new PdfAnalysisError("", ex.Message);
        }
    }

    public async Task<Result<Dictionary<string, int>, BugDiscoveryError>> CountKeywordsInTextsAsync(
        CountKeywordsInTextsCommand command,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            logger.LogDebug(
                "Counting {KeywordCount} keywords in {TextCount} text segments",
                command.Keywords.Length,
                command.Texts.Count()
            );

            if (
                command.Texts == null
                || !command.Texts.Any()
                || command.Keywords == null
                || !command.Keywords.Any()
            )
                return Result.Success<Dictionary<string, int>, BugDiscoveryError>(
                    new Dictionary<string, int>()
                );

            var keywords = command.NormalizeKeywords
                ? command.Keywords.Select(k => k.ToLowerInvariant()).ToArray()
                : command.Keywords;

            var result = PaperAnalyzer.CountKeywordsInTexts(command.Texts, keywords);

            logger.LogDebug(
                "Found {TotalOccurrences} total keyword occurrences across all texts",
                result.Values.Sum()
            );

            return Result.Success<Dictionary<string, int>, BugDiscoveryError>(result);
        }
        catch (OperationCanceledException)
        {
            return new PdfAnalysisError("", "Operation was cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error counting keywords in texts");
            return new PdfAnalysisError("", ex.Message);
        }
    }

    public async Task<Result<int, BugDiscoveryError>> ExtractBugSentencesAsync(
        ExtractBugSentencesCommand command,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            logger.LogDebug(
                "Extracting bug sentences using pattern: {Pattern}",
                command.BugPattern
            );

            if (command.PdfData?.TextLines == null)
                return new PdfAnalysisError("Unknown", "PDF data is null or empty");

            var bugPattern = new Regex(
                command.BugPattern,
                RegexOptions.IgnoreCase | RegexOptions.Compiled
            );

            var sentenceCount = PdfTextUtils.ExtractBugSentences(
                command.PdfData,
                bugPattern,
                command.ExtractionResult,
                command.AdjectivesOnly
            );

            logger.LogDebug("Extracted {SentenceCount} bug sentences", sentenceCount);

            return Result.Success<int, BugDiscoveryError>(sentenceCount);
        }
        catch (OperationCanceledException)
        {
            return new PdfAnalysisError("", "Operation was cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error extracting bug sentences");
            return new PdfAnalysisError("Unknown", ex.Message);
        }
    }
}
