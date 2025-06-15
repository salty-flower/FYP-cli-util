namespace DataCollection.Application.Options;

public class FileNameOptions
{
    public const string SectionName = "FileNames";

    public List<string> ReadmeFileNames { get; set; } =
        new()
        {
            "readme.md",
            "readme.txt",
            "readme.rst",
            "readme",
            "read.me",
            "README.md",
            "README.txt",
            "README.rst",
            "README",
            "READ.ME",
            "Readme.md",
            "Readme.txt",
            "Readme.rst",
        };

    public List<string> BugRelatedPaths { get; set; } =
        new()
        {
            "bug",
            "issue",
            "error",
            "defect",
            "fault",
            "failure",
            "test",
            "spec",
            "example",
            "demo",
            "sample",
        };
}
