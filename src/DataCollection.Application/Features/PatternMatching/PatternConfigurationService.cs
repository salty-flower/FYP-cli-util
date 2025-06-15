using DataCollection.Core.Models.Database;
using DataCollection.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataCollection.Application.Features.PatternMatching;

public interface IPatternConfigurationService
{
    Task SeedDefaultPatternsAsync(CancellationToken cancellationToken = default);
    Task<bool> HasSeededDataAsync(CancellationToken cancellationToken = default);
}

public class PatternConfigurationService(
    DataCollectionDbContext dbContext,
    ILogger<PatternConfigurationService> logger
) : IPatternConfigurationService
{
    public async Task<bool> HasSeededDataAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.PatternRules.AnyAsync(cancellationToken)
            || await dbContext.KeywordRules.AnyAsync(cancellationToken)
            || await dbContext.UrlTypeRules.AnyAsync(cancellationToken)
            || await dbContext.ConfigurationRules.AnyAsync(cancellationToken);
    }

    public async Task SeedDefaultPatternsAsync(CancellationToken cancellationToken = default)
    {
        if (await HasSeededDataAsync(cancellationToken))
        {
            logger.LogInformation("Pattern data already exists, skipping seeding");
            return;
        }

        logger.LogInformation("Seeding default pattern configuration");

        await SeedBugTrackingPatternsAsync(cancellationToken);
        await SeedRepositoryPatternsAsync(cancellationToken);
        await SeedKeywordRulesAsync(cancellationToken);
        await SeedUrlTypeRulesAsync(cancellationToken);
        await SeedAdditionalPatternsAsync(cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Pattern seeding completed");
    }

    private async Task SeedBugTrackingPatternsAsync(CancellationToken cancellationToken)
    {
        var patterns = new[]
        {
            new PatternRule
            {
                Category = "BugTracking",
                Pattern = @"https?://github\.com/[\w\-\.]+/[\w\-\.]+/issues",
                Description = "GitHub Issues",
                BaseConfidence = 0.95,
                Priority = 100,
            },
            new PatternRule
            {
                Category = "BugTracking",
                Pattern = @"https?://bugs\.[\w\-\.]+",
                Description = "Generic Bug Tracker",
                BaseConfidence = 0.90,
                Priority = 200,
            },
            new PatternRule
            {
                Category = "BugTracking",
                Pattern = @"https?://[\w\-\.]*jira[\w\-\.]*",
                Description = "Jira",
                BaseConfidence = 0.92,
                Priority = 150,
            },
            new PatternRule
            {
                Category = "BugTracking",
                Pattern = @"https?://[\w\-\.]*bugzilla[\w\-\.]*",
                Description = "Bugzilla",
                BaseConfidence = 0.93,
                Priority = 140,
            },
            new PatternRule
            {
                Category = "BugTracking",
                Pattern = @"https?://[\w\-\.]*mantis[\w\-\.]*",
                Description = "MantisBT",
                BaseConfidence = 0.90,
                Priority = 180,
            },
            new PatternRule
            {
                Category = "BugTracking",
                Pattern = @"https?://[\w\-\.]*redmine[\w\-\.]*",
                Description = "Redmine",
                BaseConfidence = 0.88,
                Priority = 190,
            },
            new PatternRule
            {
                Category = "BugTracking",
                Pattern = @"https?://[\w\-\.]*trac[\w\-\.]*",
                Description = "Trac",
                BaseConfidence = 0.85,
                Priority = 210,
            },
            new PatternRule
            {
                Category = "BugTracking",
                Pattern = @"https?://[\w\-\.]*youtrack[\w\-\.]*",
                Description = "YouTrack",
                BaseConfidence = 0.90,
                Priority = 170,
            },
            new PatternRule
            {
                Category = "BugTracking",
                Pattern = @"https?://[\w\-\.]*fogbugz[\w\-\.]*",
                Description = "FogBugz",
                BaseConfidence = 0.85,
                Priority = 220,
            },
        };

        await dbContext.PatternRules.AddRangeAsync(patterns, cancellationToken);
    }

    private async Task SeedRepositoryPatternsAsync(CancellationToken cancellationToken)
    {
        var patterns = new[]
        {
            new PatternRule
            {
                Category = "Repository",
                Pattern =
                    @"https?://github\.com/[\w\-\.]+/[\w\-\.]+(?!/issues|/wiki|/releases|/actions|/security|/insights|/settings|/projects|/discussions)",
                Description = "GitHub Repository",
                BaseConfidence = 0.95,
                Priority = 100,
            },
            new PatternRule
            {
                Category = "Repository",
                Pattern = @"https?://gitlab\.com/[\w\-\.]+/[\w\-\.]+",
                Description = "GitLab Repository",
                BaseConfidence = 0.92,
                Priority = 110,
            },
            new PatternRule
            {
                Category = "Repository",
                Pattern = @"https?://bitbucket\.org/[\w\-\.]+/[\w\-\.]+",
                Description = "Bitbucket Repository",
                BaseConfidence = 0.90,
                Priority = 120,
            },
            new PatternRule
            {
                Category = "Repository",
                Pattern = @"https?://sourceforge\.net/projects/[\w\-\.]+",
                Description = "SourceForge Project",
                BaseConfidence = 0.85,
                Priority = 130,
            },
            new PatternRule
            {
                Category = "Repository",
                Pattern = @"https?://code\.google\.com/p/[\w\-\.]+",
                Description = "Google Code",
                BaseConfidence = 0.80,
                Priority = 140,
            },
            new PatternRule
            {
                Category = "Repository",
                Pattern = @"https?://launchpad\.net/[\w\-\.]+",
                Description = "Launchpad",
                BaseConfidence = 0.75,
                Priority = 150,
            },
            new PatternRule
            {
                Category = "Repository",
                Pattern = @"https?://codeplex\.com/[\w\-\.]+",
                Description = "CodePlex",
                BaseConfidence = 0.70,
                Priority = 160,
            },
            new PatternRule
            {
                Category = "Repository",
                Pattern = @"https?://git\.[\w\-\.]+/[\w\-\.]+/[\w\-\.]+",
                Description = "Git Repository",
                BaseConfidence = 0.85,
                Priority = 170,
            },
            new PatternRule
            {
                Category = "Repository",
                Pattern = @"https?://svn\.[\w\-\.]+",
                Description = "Subversion",
                BaseConfidence = 0.75,
                Priority = 180,
            },
            new PatternRule
            {
                Category = "Repository",
                Pattern = @"https?://hg\.[\w\-\.]+",
                Description = "Mercurial",
                BaseConfidence = 0.75,
                Priority = 190,
            },
        };

        await dbContext.PatternRules.AddRangeAsync(patterns, cancellationToken);
    }

    private async Task SeedKeywordRulesAsync(CancellationToken cancellationToken)
    {
        var artifactKeywords = new[]
        {
            "artifact",
            "repository",
            "repo",
            "source code",
            "implementation",
            "codebase",
            "project",
            "software",
            "program",
            "application",
            "system",
            "tool",
            "library",
            "framework",
            "dataset",
            "data",
            "benchmark",
            "evaluation",
            "experiment",
            "github",
            "gitlab",
            "bitbucket",
            "sourceforge",
            "available at",
            "can be found",
            "accessible",
            "download",
            "obtain",
            "retrieve",
            "access",
            "provided",
            "supplement",
            "supplementary",
            "material",
            "materials",
            "resource",
            "resources",
            "code",
            "scripts",
            "files",
            "documentation",
            "manual",
            "guide",
            "tutorial",
            "readme",
            "license",
            "copyright",
            "open source",
            "free software",
        };

        var bugListKeywords = new[]
        {
            "bug list",
            "bug report",
            "bug track",
            "issue list",
            "issue report",
            "issue track",
            "defect list",
            "defect report",
            "fault list",
            "error list",
            "problem list",
            "failure list",
            "exception list",
            "crash list",
            "vulnerability list",
            "security issue",
            "security bug",
            "patch list",
            "fix list",
            "ticket list",
        };

        var keywordRules = new List<KeywordRule>();

        foreach (var keyword in artifactKeywords)
        {
            keywordRules.Add(
                new KeywordRule
                {
                    Category = "Artifact",
                    Keyword = keyword,
                    Description = $"Artifact keyword: {keyword}",
                    BaseConfidence = 0.75,
                    Priority = 100,
                }
            );
        }

        foreach (var keyword in bugListKeywords)
        {
            keywordRules.Add(
                new KeywordRule
                {
                    Category = "BugList",
                    Keyword = keyword,
                    Description = $"Bug list keyword: {keyword}",
                    BaseConfidence = 0.80,
                    Priority = 100,
                }
            );
        }

        await dbContext.KeywordRules.AddRangeAsync(keywordRules, cancellationToken);
    }

    private async Task SeedUrlTypeRulesAsync(CancellationToken cancellationToken)
    {
        var urlTypeRules = new[]
        {
            new UrlTypeRule
            {
                Pattern = @"github\.com.*?/issues",
                Type = "GitHub Issues",
                Priority = 100,
            },
            new UrlTypeRule
            {
                Pattern = @"github\.com",
                Type = "GitHub",
                Priority = 95,
            },
            new UrlTypeRule
            {
                Pattern = @"gitlab\.com",
                Type = "GitLab",
                Priority = 90,
            },
            new UrlTypeRule
            {
                Pattern = @"bitbucket\.org",
                Type = "Bitbucket",
                Priority = 85,
            },
            new UrlTypeRule
            {
                Pattern = @"sourceforge\.net",
                Type = "SourceForge",
                Priority = 80,
            },
            new UrlTypeRule
            {
                Pattern = @"jira",
                Type = "Jira",
                Priority = 95,
            },
            new UrlTypeRule
            {
                Pattern = @"bugzilla",
                Type = "Bugzilla",
                Priority = 90,
            },
            new UrlTypeRule
            {
                Pattern = @"mantis",
                Type = "MantisBT",
                Priority = 85,
            },
            new UrlTypeRule
            {
                Pattern = @"redmine",
                Type = "Redmine",
                Priority = 80,
            },
            new UrlTypeRule
            {
                Pattern = @"trac",
                Type = "Trac",
                Priority = 75,
            },
            new UrlTypeRule
            {
                Pattern = @"youtrack",
                Type = "YouTrack",
                Priority = 85,
            },
            new UrlTypeRule
            {
                Pattern = @"fogbugz",
                Type = "FogBugz",
                Priority = 75,
            },
            new UrlTypeRule
            {
                Pattern = @"bugs\.",
                Type = "Custom Bug Tracker",
                Priority = 70,
            },
            new UrlTypeRule
            {
                Pattern = @"\.git",
                Type = "Git Repository",
                Priority = 60,
            },
            new UrlTypeRule
            {
                Pattern = @"svn\.",
                Type = "Subversion",
                Priority = 55,
            },
            new UrlTypeRule
            {
                Pattern = @"hg\.",
                Type = "Mercurial",
                Priority = 55,
            },
        };

        await dbContext.UrlTypeRules.AddRangeAsync(urlTypeRules, cancellationToken);
    }

    private async Task SeedAdditionalPatternsAsync(CancellationToken cancellationToken)
    {
        // Add patterns that were in BugListPatterns.cs but not in the existing seeding

        // GitHub URL extraction patterns
        var gitHubPatterns = new[]
        {
            new PatternRule
            {
                Category = "GitHubUrl",
                Pattern = @"https?://github\.com/([\w\-\.]+)/([\w\-\.]+)",
                Description = "GitHub URL extraction with groups",
                BaseConfidence = 0.95,
                Priority = 100,
            },
            new PatternRule
            {
                Category = "GitHubUrl",
                Pattern = @"https?://www\.github\.com/([\w\-\.]+)/([\w\-\.]+)",
                Description = "GitHub WWW URL extraction with groups",
                BaseConfidence = 0.95,
                Priority = 95,
            },
            new PatternRule
            {
                Category = "GitHubUrl",
                Pattern = @"github\.com/([\w\-\.]+)/([\w\-\.]+)",
                Description = "GitHub URL without protocol",
                BaseConfidence = 0.90,
                Priority = 90,
            },
            new PatternRule
            {
                Category = "GitHubUrl",
                Pattern = @"www\.github\.com/([\w\-\.]+)/([\w\-\.]+)",
                Description = "GitHub WWW URL without protocol",
                BaseConfidence = 0.90,
                Priority = 85,
            },
        };

        // Bug list text identification patterns
        var bugListTextPatterns = new[]
        {
            new PatternRule
            {
                Category = "BugListText",
                Pattern = @"(?i)bug\s*(?:list|report|track|id|number|#)",
                Description = "Bug list text pattern",
                BaseConfidence = 0.85,
                Priority = 100,
            },
            new PatternRule
            {
                Category = "BugListText",
                Pattern = @"(?i)issue\s*(?:list|report|track|id|number|#)",
                Description = "Issue list text pattern",
                BaseConfidence = 0.85,
                Priority = 95,
            },
            new PatternRule
            {
                Category = "BugListText",
                Pattern = @"(?i)defect\s*(?:list|report|track|id|number|#)",
                Description = "Defect list text pattern",
                BaseConfidence = 0.80,
                Priority = 90,
            },
            new PatternRule
            {
                Category = "BugListText",
                Pattern = @"(?i)fault\s*(?:list|report|track|id|number|#)",
                Description = "Fault list text pattern",
                BaseConfidence = 0.80,
                Priority = 85,
            },
            new PatternRule
            {
                Category = "BugListText",
                Pattern = @"(?i)error\s*(?:list|report|track|id|number|#)",
                Description = "Error list text pattern",
                BaseConfidence = 0.80,
                Priority = 80,
            },
            new PatternRule
            {
                Category = "BugListText",
                Pattern = @"(?i)problem\s*(?:list|report|track|id|number|#)",
                Description = "Problem list text pattern",
                BaseConfidence = 0.75,
                Priority = 75,
            },
            new PatternRule
            {
                Category = "BugListText",
                Pattern = @"(?i)failure\s*(?:list|report|track|id|number|#)",
                Description = "Failure list text pattern",
                BaseConfidence = 0.75,
                Priority = 70,
            },
            new PatternRule
            {
                Category = "BugListText",
                Pattern = @"(?i)exception\s*(?:list|report|track|id|number|#)",
                Description = "Exception list text pattern",
                BaseConfidence = 0.75,
                Priority = 65,
            },
            new PatternRule
            {
                Category = "BugListText",
                Pattern = @"(?i)crash\s*(?:list|report|track|id|number|#)",
                Description = "Crash list text pattern",
                BaseConfidence = 0.75,
                Priority = 60,
            },
            new PatternRule
            {
                Category = "BugListText",
                Pattern = @"(?i)vulnerability\s*(?:list|report|track|id|number|#)",
                Description = "Vulnerability list text pattern",
                BaseConfidence = 0.85,
                Priority = 55,
            },
            new PatternRule
            {
                Category = "BugListText",
                Pattern = @"(?i)security\s*(?:issue|bug|flaw)",
                Description = "Security issue text pattern",
                BaseConfidence = 0.85,
                Priority = 50,
            },
            new PatternRule
            {
                Category = "BugListText",
                Pattern = @"(?i)patch\s*(?:list|track|id|number|#)",
                Description = "Patch list text pattern",
                BaseConfidence = 0.75,
                Priority = 45,
            },
            new PatternRule
            {
                Category = "BugListText",
                Pattern = @"(?i)fix\s*(?:list|track|id|number|#)",
                Description = "Fix list text pattern",
                BaseConfidence = 0.75,
                Priority = 40,
            },
            new PatternRule
            {
                Category = "BugListText",
                Pattern = @"(?i)ticket\s*(?:list|track|id|number|#)",
                Description = "Ticket list text pattern",
                BaseConfidence = 0.75,
                Priority = 35,
            },
        };

        // Issue number patterns
        var issueNumberPatterns = new[]
        {
            new PatternRule
            {
                Category = "IssueNumber",
                Pattern = @"#\d+",
                Description = "Hash-prefixed issue number",
                BaseConfidence = 0.90,
                Priority = 100,
            },
            new PatternRule
            {
                Category = "IssueNumber",
                Pattern = @"issue\s*#?\s*\d+",
                Description = "Issue number with optional hash",
                BaseConfidence = 0.85,
                Priority = 95,
            },
            new PatternRule
            {
                Category = "IssueNumber",
                Pattern = @"bug\s*#?\s*\d+",
                Description = "Bug number with optional hash",
                BaseConfidence = 0.85,
                Priority = 90,
            },
            new PatternRule
            {
                Category = "IssueNumber",
                Pattern = @"ticket\s*#?\s*\d+",
                Description = "Ticket number with optional hash",
                BaseConfidence = 0.80,
                Priority = 85,
            },
            new PatternRule
            {
                Category = "IssueNumber",
                Pattern = @"defect\s*#?\s*\d+",
                Description = "Defect number with optional hash",
                BaseConfidence = 0.80,
                Priority = 80,
            },
            new PatternRule
            {
                Category = "IssueNumber",
                Pattern = @"fault\s*#?\s*\d+",
                Description = "Fault number with optional hash",
                BaseConfidence = 0.75,
                Priority = 75,
            },
            new PatternRule
            {
                Category = "IssueNumber",
                Pattern = @"problem\s*#?\s*\d+",
                Description = "Problem number with optional hash",
                BaseConfidence = 0.75,
                Priority = 70,
            },
            new PatternRule
            {
                Category = "IssueNumber",
                Pattern = @"error\s*#?\s*\d+",
                Description = "Error number with optional hash",
                BaseConfidence = 0.75,
                Priority = 65,
            },
            new PatternRule
            {
                Category = "IssueNumber",
                Pattern = @"failure\s*#?\s*\d+",
                Description = "Failure number with optional hash",
                BaseConfidence = 0.70,
                Priority = 60,
            },
            new PatternRule
            {
                Category = "IssueNumber",
                Pattern = @"exception\s*#?\s*\d+",
                Description = "Exception number with optional hash",
                BaseConfidence = 0.70,
                Priority = 55,
            },
        };

        // General utility patterns
        var utilityPatterns = new[]
        {
            new PatternRule
            {
                Category = "Utility",
                Pattern = @"https?://[^\s<>\)\]\}""']+",
                Description = "General URL extraction pattern",
                BaseConfidence = 0.95,
                Priority = 100,
            },
            new PatternRule
            {
                Category = "Utility",
                Pattern = @"[^\d]",
                Description = "Non-alphanumeric pattern for cleaning",
                BaseConfidence = 1.0,
                Priority = 100,
            },
        };

        // Add missing repository patterns from BugListPatterns.cs
        var additionalRepoPatterns = new[]
        {
            new PatternRule
            {
                Category = "Repository",
                Pattern = @"https?://[\w\-\.]+\.git\.[\w\-\.]+",
                Description = "Git repository with git subdomain",
                BaseConfidence = 0.80,
                Priority = 200,
            },
            new PatternRule
            {
                Category = "Repository",
                Pattern = @"https?://bazaar\.[\w\-\.]+",
                Description = "Bazaar repository",
                BaseConfidence = 0.70,
                Priority = 210,
            },
            new PatternRule
            {
                Category = "Repository",
                Pattern = @"https?://fossil\.[\w\-\.]+",
                Description = "Fossil repository",
                BaseConfidence = 0.70,
                Priority = 220,
            },
            new PatternRule
            {
                Category = "Repository",
                Pattern = @"https?://darcs\.[\w\-\.]+",
                Description = "Darcs repository",
                BaseConfidence = 0.65,
                Priority = 230,
            },
            new PatternRule
            {
                Category = "Repository",
                Pattern = @"https?://cvs\.[\w\-\.]+",
                Description = "CVS repository",
                BaseConfidence = 0.65,
                Priority = 240,
            },
        };

        var allPatterns = gitHubPatterns
            .Concat(bugListTextPatterns)
            .Concat(issueNumberPatterns)
            .Concat(utilityPatterns)
            .Concat(additionalRepoPatterns);

        await dbContext.PatternRules.AddRangeAsync(allPatterns, cancellationToken);
    }
}
