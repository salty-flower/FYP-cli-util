using System.Text.Json;
using DataCollection.Core.Models;
using DataCollection.Core.Models.Database;
using DataCollection.Core.Models.IssueTracker;
using DataCollection.Infrastructure.Options;
using DataCollection.Infrastructure.Serialization;
using Microsoft.Build.Utilities;
using Serilog;

namespace DataCollection.Infrastructure.Extensions;

public static class EntityExtensions
{
    public static Paper ToPaper(this PaperEntity entity) =>
        new()
        {
            Title = entity.Title,
            Authors =
                JsonSerializer.Deserialize(entity.Authors, AppJsonContext.Default.StringArray)
                ?? [],
            Abstract = entity.Abstract,
            Url = entity.Url,
            Doi = entity.Doi,
        };

    public static PaperEntity ToEntity(this Paper paper, JobName jobName) =>
        new()
        {
            Title = paper.Title,
            Authors = JsonSerializer.Serialize(paper.Authors, AppJsonContext.Default.StringArray),
            Abstract = paper.Abstract ?? string.Empty,
            Url = paper.Url,
            Doi = paper.Doi,
            Conf = jobName.Conf,
            Year = jobName.Year,
        };

    public static PdfData ToPdfData(this PdfDataEntity entity) =>
        new()
        {
            FileName = entity.FileName,
            Texts =
                JsonSerializer.Deserialize(entity.Texts, AppJsonContext.Default.StringArray) ?? [],
            TextLines =
                JsonSerializer.Deserialize(
                    entity.TextLines,
                    AppJsonContext.Default.MatchObjectArrayArray
                ) ?? [],
        };

    public static PdfDataEntity ToEntity(
        this PdfData pdfData,
        JobName jobName,
        int? paperId = null
    ) =>
        new()
        {
            FileName = pdfData.FileName,
            Texts = JsonSerializer.Serialize(pdfData.Texts, AppJsonContext.Default.StringArray),
            TextLines = JsonSerializer.Serialize(
                pdfData.TextLines,
                AppJsonContext.Default.MatchObjectArrayArray
            ),
            Conf = jobName.Conf,
            Year = jobName.Year,
            PaperId = paperId,
        };

    public static CachedAnalysisResult? ToCachedAnalysisResult(this IssueAnalysisEntity entity)
    {
        // check if we can deserialize IssueAnalysisResponse, because the schema might have changed.
        // if cannot, we give a warning and set null.

        try
        {
            return new()
            {
                Owner = entity.Owner,
                Repository = entity.Repository,
                IssueNumber = entity.IssueNumber,
                Status = entity.Status,
                Analysis = JsonSerializer.Deserialize(
                    entity.AnalysisJson,
                    AppJsonContext.Default.IssueAnalysisResponse
                )!,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static IssueAnalysisEntity ToEntity(this CachedAnalysisResult result) =>
        new()
        {
            Owner = result.Owner,
            Repository = result.Repository,
            IssueNumber = result.IssueNumber,
            Status = result.Status,
            AnalysisJson = JsonSerializer.Serialize(
                result.Analysis,
                AppJsonContext.Default.IssueAnalysisResponse
            ),
        };
}
