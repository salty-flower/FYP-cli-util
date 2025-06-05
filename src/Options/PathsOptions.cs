using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using ConsoleAppFramework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DataCollection.Options;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
public partial class PathsOptions
{
    /// <summary>
    /// Base directory for all data files. Defaults to $(SolutionDir)\data
    /// </summary>
    public string BaseDir { get; set; } = Path.Combine(BuildConstants.SolutionDirectory, "data");

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
    private string CombineWithBase(string fieldValue) =>
        Path.Combine(
            BaseDir,
            ConsoleApp
                .ServiceProvider!.GetRequiredService<IOptionsSnapshot<RootOptions>>()
                .Value.JobName,
            fieldValue
        );

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
