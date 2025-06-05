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
            || await dbContext.UrlTypeRules.AnyAsync(cancellationToken);
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
}
