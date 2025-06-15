namespace DataCollection.Application.Options;

public class KnownHostOptions
{
    public const string SectionName = "KnownHosts";

    public List<string> RepositoryHosts { get; set; } =
        new()
        {
            "github.com",
            "gitlab.com",
            "bitbucket.org",
            "sourceforge.net",
            "codeplex.com",
            "gitee.com",
            "gitea.io",
        };
}
