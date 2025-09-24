namespace DataCollection.Infrastructure.Options;

public class ParallelismOptions
{
    public int SectionProcessing { get; init; } = 3;
    public int PaperEnrichment { get; init; } = 5;
    public int PaperDownloading { get; init; } = 5;
    public int IssueProcessing { get; init; } = 10;
    public int IssueDataPreparation { get; init; } = 5;
    public int BatchResultsProcessing { get; init; } = 8;
}
