using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace DataCollection.Core.Options;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
public partial class PathsOptions
{
    private string? _jobName;

    /// <summary>
    /// Base directory for all data files. Defaults to a data folder
    /// </summary>
    public string BaseDir { get; set; } = "data";

    /// <summary>
    /// Job name for the current operation
    /// </summary>
    public string JobName
    {
        get => _jobName ?? string.Empty;
        set => _jobName = value;
    }

    public string PaperMetadataDir
    {
        init;
        get => CombineWithBase(field);
    } = "paper-metadata";

    public string PdfDataDir
    {
        get => CombineWithBase(field);
        init;
    } = "pdfdata";

    public string PaperBinDir
    {
        get => CombineWithBase(field);
        init;
    } = "paper-bin";

    public string IssueTrackerDir
    {
        get => CombineWithBase(field);
        init;
    } = "issue-tracker";

    public string IssueRepoDir
    {
        get => Path.Combine(IssueTrackerDir, field);
        init;
    } = "repos";

    public string IssueProfileDir
    {
        get => Path.Combine(IssueTrackerDir, field);
        init;
    } = "profiles";

    public string IssueAnalysisDir
    {
        get => Path.Combine(IssueTrackerDir, field);
        init;
    } = "analysis";

    public string IssuePromptCacheDir
    {
        get => Path.Combine(IssueTrackerDir, field);
        init;
    } = "prompts";

    /// <summary>
    /// Path to Python DLL
    /// </summary>
    public required string PythonDLL { get; init; }

    /// <summary>
    /// Returns the combined path of "{BaseDir}/{JobName}/{fieldValue}"
    /// </summary>
    /// <param name="fieldValue"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string CombineWithBase(string fieldValue) => Path.Combine(BaseDir, JobName, fieldValue);

    public void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(PaperMetadataDir);
        Directory.CreateDirectory(PdfDataDir);
        Directory.CreateDirectory(PaperBinDir);
        Directory.CreateDirectory(IssueTrackerDir);
        Directory.CreateDirectory(IssueRepoDir);
        Directory.CreateDirectory(IssueProfileDir);
        Directory.CreateDirectory(IssueAnalysisDir);
        Directory.CreateDirectory(IssuePromptCacheDir);
    }
}
