using System.Text.Json;
using DataCollection.Models.IssueTracker;
using DataCollection.Models.IssueTracker.Responses;

namespace DataCollection.Models.Database;

public static class Extensions
{
    public static Paper ToPaper(this PaperEntity entity)
    {
        return new Paper
        {
            Title = entity.Title,
            Authors = JsonSerializer.Deserialize<string[]>(entity.Authors) ?? [],
            Abstract = entity.Abstract,
            Url = entity.Url,
            Doi = entity.Doi,
        };
    }

    public static PaperEntity ToEntity(this Paper paper, string conf, int year)
    {
        return new PaperEntity
        {
            Title = paper.Title,
            Authors = JsonSerializer.Serialize(paper.Authors),
            Abstract = paper.Abstract,
            Url = paper.Url,
            Doi = paper.Doi,
            Conf = conf,
            Year = year,
        };
    }

    public static PdfData ToPdfData(this PdfDataEntity entity)
    {
        return new PdfData
        {
            FileName = entity.FileName,
            Texts = JsonSerializer.Deserialize<string[]>(entity.Texts) ?? [],
            TextLines = JsonSerializer.Deserialize<MatchObject[][]>(entity.TextLines) ?? [],
        };
    }

    public static PdfDataEntity ToEntity(
        this PdfData pdfData,
        string conf,
        int year,
        int? paperId = null
    )
    {
        return new PdfDataEntity
        {
            FileName = pdfData.FileName,
            Texts = JsonSerializer.Serialize(pdfData.Texts),
            TextLines = JsonSerializer.Serialize(pdfData.TextLines),
            Conf = conf,
            Year = year,
            PaperId = paperId,
        };
    }

    public static CachedAnalysisResult ToCachedAnalysisResult(this IssueAnalysisEntity entity)
    {
        var analysis =
            JsonSerializer.Deserialize<IssueAnalysisResponse>(entity.AnalysisJson)
            ?? throw new InvalidOperationException(
                $"Failed to deserialize analysis for issue {entity.Owner}/{entity.Repository}#{entity.IssueNumber}"
            );

        return new CachedAnalysisResult
        {
            Owner = entity.Owner,
            Repository = entity.Repository,
            IssueNumber = entity.IssueNumber,
            Status = entity.Status,
            Analysis = analysis,
        };
    }

    public static IssueAnalysisEntity ToEntity(this CachedAnalysisResult result)
    {
        return new IssueAnalysisEntity
        {
            Owner = result.Owner,
            Repository = result.Repository,
            IssueNumber = result.IssueNumber,
            Status = result.Status,
            AnalysisJson = JsonSerializer.Serialize(result.Analysis),
        };
    }
}
