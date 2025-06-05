namespace DataCollection.Utils;

/// <summary>
/// Regular expression patterns for bug list discovery
/// </summary>
public static class BugListPatterns
{
    // Bug tracking system URL patterns
    public static readonly string[] BugTrackingPatterns =
    [
        @"https?://github\.com/[\w\-\.]+/[\w\-\.]+/issues",
        @"https?://bugs\.[\w\-\.]+",
        @"https?://[\w\-\.]*jira[\w\-\.]*",
        @"https?://[\w\-\.]*bugzilla[\w\-\.]*",
        @"https?://[\w\-\.]*mantis[\w\-\.]*",
        @"https?://[\w\-\.]*redmine[\w\-\.]*",
        @"https?://[\w\-\.]*trac[\w\-\.]*",
        @"https?://[\w\-\.]*youtrack[\w\-\.]*",
        @"https?://[\w\-\.]*fogbugz[\w\-\.]*",
    ];

    // Repository URL patterns
    public static readonly string[] RepositoryPatterns =
    [
        @"https?://github\.com/[\w\-\.]+/[\w\-\.]+(?!/issues|/wiki|/releases|/actions|/security|/insights|/settings|/projects|/discussions)",
        @"https?://gitlab\.com/[\w\-\.]+/[\w\-\.]+",
        @"https?://bitbucket\.org/[\w\-\.]+/[\w\-\.]+",
        @"https?://sourceforge\.net/projects/[\w\-\.]+",
        @"https?://code\.google\.com/p/[\w\-\.]+",
        @"https?://launchpad\.net/[\w\-\.]+",
        @"https?://codeplex\.com/[\w\-\.]+",
        @"https?://git\.[\w\-\.]+/[\w\-\.]+/[\w\-\.]+",
        @"https?://[\w\-\.]+\.git\.[\w\-\.]+",
        @"https?://svn\.[\w\-\.]+",
        @"https?://hg\.[\w\-\.]+",
        @"https?://bazaar\.[\w\-\.]+",
        @"https?://fossil\.[\w\-\.]+",
        @"https?://darcs\.[\w\-\.]+",
        @"https?://cvs\.[\w\-\.]+",
    ];

    // GitHub URL extraction patterns
    public static readonly string[] GitHubUrlPatterns =
    [
        @"https?://github\.com/([\w\-\.]+)/([\w\-\.]+)",
        @"https?://www\.github\.com/([\w\-\.]+)/([\w\-\.]+)",
        @"github\.com/([\w\-\.]+)/([\w\-\.]+)",
        @"www\.github\.com/([\w\-\.]+)/([\w\-\.]+)",
    ];

    // Bug list identification patterns in text
    public static readonly string[] BugListTextPatterns =
    [
        @"(?i)bug\s*(?:list|report|track|id|number|#)",
        @"(?i)issue\s*(?:list|report|track|id|number|#)",
        @"(?i)defect\s*(?:list|report|track|id|number|#)",
        @"(?i)fault\s*(?:list|report|track|id|number|#)",
        @"(?i)error\s*(?:list|report|track|id|number|#)",
        @"(?i)problem\s*(?:list|report|track|id|number|#)",
        @"(?i)failure\s*(?:list|report|track|id|number|#)",
        @"(?i)exception\s*(?:list|report|track|id|number|#)",
        @"(?i)crash\s*(?:list|report|track|id|number|#)",
        @"(?i)vulnerability\s*(?:list|report|track|id|number|#)",
        @"(?i)security\s*(?:issue|bug|flaw)",
        @"(?i)patch\s*(?:list|track|id|number|#)",
        @"(?i)fix\s*(?:list|track|id|number|#)",
        @"(?i)ticket\s*(?:list|track|id|number|#)",
    ];

    // Issue number patterns in text
    public static readonly string[] IssueNumberPatterns =
    [
        @"#\d+",
        @"issue\s*#?\s*\d+",
        @"bug\s*#?\s*\d+",
        @"ticket\s*#?\s*\d+",
        @"defect\s*#?\s*\d+",
        @"fault\s*#?\s*\d+",
        @"problem\s*#?\s*\d+",
        @"error\s*#?\s*\d+",
        @"failure\s*#?\s*\d+",
        @"exception\s*#?\s*\d+",
    ];

    // General URL pattern for extracting URLs from text
    public const string GeneralUrlPattern = @"https?://[^\s<>\)\]\}""']+";

    // Pattern for extracting non-alphanumeric characters (for cleaning)
    public const string NonAlphanumericPattern = @"[^\d]";
}
