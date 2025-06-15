namespace DataCollection.Application.Options;

public class LimitOptions
{
    public const string SectionName = "Limits";

    public int MaxRepositoriesPerSearch { get; set; } = 50;
    public int MaxIssuesPerRepository { get; set; } = 1000;
    public int MaxFilesPerRepository { get; set; } = 500;
    public int MaxSearchResults { get; set; } = 100;
}
