using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace DataCollection.Infrastructure.Options;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
public partial class PathsOptions
{
    /// <summary>
    /// Base directory for all data files. Defaults to a data folder
    /// </summary>
    public string BaseDir { get; set; } = "data";

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

    /// <summary>
    /// Path to Python DLL
    /// </summary>
    public required string PythonDLL { get; init; }

    /// <summary>
    /// Returns the combined path of "{BaseDir}/{fieldValue}"
    /// </summary>
    /// <param name="fieldValue"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string CombineWithBase(string fieldValue) => Path.Combine(BaseDir, fieldValue);

    public void EnsureDirectoriesExist() => Directory.CreateDirectory(PaperBinDir);
}
