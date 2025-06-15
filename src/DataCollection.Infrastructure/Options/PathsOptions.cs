using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace DataCollection.Infrastructure.Options;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
public class PathsOptions
{
    /// <summary>
    /// Base directory for all data files. Defaults to a data folder
    /// </summary>
    public string BaseDir { get; init; } = "data";

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

    public required string PythonDLL { get; init; }

    /// <param name="fieldValue"></param>
    /// <returns>The combined path of "{BaseDir}/{fieldValue}"</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string CombineWithBase(string fieldValue) =>
        Path.Combine(BuildConstants.SolutionDirectory, BaseDir, fieldValue);

    public void EnsureDirectoriesExist() => Directory.CreateDirectory(PaperBinDir);
}
