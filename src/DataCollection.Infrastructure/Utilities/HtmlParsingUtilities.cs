using System.Text.RegularExpressions;

namespace DataCollection.Infrastructure.Utilities;

public static class HtmlParsingUtilities
{
    private static readonly Regex MetaTagPattern = new(
        @"<meta\s+name=""([^""]+)""\s+content=""([^""]*)""\s*/?>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex IdElementPattern = new(
        @"<[^>]*\bid=""([^""]+)""[^>]*>([^<]*(?:<[^>]*>[^<]*)*?)</[^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex UserHoverPattern = new(
        @"<span[^>]*\brel=""([^""]+)""[^>]*>([^<]*)</span>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex TitlePattern = new(
        @"<title[^>]*>([^<]*)</title>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex ElementByIdPattern = new(
        @"<[^>]*\bid=""([^""]+)""[^>]*>(.*?)</[^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline
    );

    public static Dictionary<string, string> ExtractMetaTags(string html)
    {
        var metaTags = new Dictionary<string, string>();

        var matches = MetaTagPattern.Matches(html);
        foreach (Match match in matches)
        {
            if (match.Success && match.Groups.Count == 3)
            {
                metaTags[match.Groups[1].Value] = match.Groups[2].Value;
            }
        }

        return metaTags;
    }

    public static string? ExtractElementContent(string html, string elementId)
    {
        // More robust pattern that handles nested elements
        var pattern = $@"<[^>]*\bid=""{Regex.Escape(elementId)}""[^>]*>(.*?)</[^>]*>";
        var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var match = regex.Match(html);
        if (match.Success && match.Groups.Count > 1)
        {
            var content = match.Groups[1].Value;
            // Remove HTML tags and clean up whitespace
            content = Regex.Replace(content, @"<[^>]*>", " ");
            content = Regex.Replace(content, @"\s+", " ").Trim();
            return string.IsNullOrEmpty(content) ? null : content;
        }

        return null;
    }

    public static List<(string username, string displayName)> ExtractUserReferences(string html)
    {
        var users = new List<(string, string)>();

        var matches = UserHoverPattern.Matches(html);
        foreach (Match match in matches)
        {
            if (match.Success && match.Groups.Count == 3)
            {
                var username = match.Groups[1].Value;
                var displayName = match.Groups[2].Value.Trim();
                users.Add((username, displayName));
            }
        }

        return users;
    }

    public static string? ExtractTitle(string html)
    {
        var match = TitlePattern.Match(html);
        if (match.Success && match.Groups.Count > 1)
        {
            return Regex.Replace(match.Groups[1].Value.Trim(), @"\s+", " ");
        }

        return null;
    }

    public static bool IsAuthenticationRequired(string html, int statusCode)
    {
        // Check for redirect to login page
        if (statusCode == 302)
        {
            return html.Contains("login.jsp") || html.Contains("permissionViolation=true");
        }

        // Check for login-related content in HTML
        return html.Contains("login.jsp")
            || html.Contains("You are not logged in")
            || html.Contains("authentication required")
            || html.Contains("permission denied");
    }

    public static bool IsUserAdmin(Dictionary<string, string> metaTags)
    {
        return metaTags.TryGetValue("ajs-is-sysadmin", out var isSysAdmin) && isSysAdmin == "true"
            || metaTags.TryGetValue("ajs-is-admin", out var isAdmin) && isAdmin == "true";
    }

    public static string? GetCurrentUser(Dictionary<string, string> metaTags)
    {
        if (
            metaTags.TryGetValue("ajs-remote-user", out var remoteUser)
            && !string.IsNullOrEmpty(remoteUser)
        )
        {
            return remoteUser;
        }

        if (
            metaTags.TryGetValue("ajs-remote-user-fullname", out var fullName)
            && !string.IsNullOrEmpty(fullName)
        )
        {
            return fullName;
        }

        return null;
    }

    public static string CleanTextContent(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Remove HTML entities and extra whitespace
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ").Trim();

        return text;
    }

    public static Dictionary<string, string> ExtractIssueFields(string html)
    {
        var fields = new Dictionary<string, string>();

        // Common Jira field IDs
        var fieldIds = new[]
        {
            "summary-val",
            "assignee-val",
            "reporter-val",
            "status-val",
            "priority-val",
            "resolution-val",
            "components-val",
            "versions-val",
            "fixVersions-val",
            "labels-val",
            "description-val",
        };

        foreach (var fieldId in fieldIds)
        {
            var content = ExtractElementContent(html, fieldId);
            if (!string.IsNullOrEmpty(content))
            {
                fields[fieldId] = CleanTextContent(content);
            }
        }

        return fields;
    }

    public static List<string> ExtractCommentIds(string html)
    {
        var commentIds = new List<string>();

        // Look for comment IDs in typical Jira comment structure
        var commentPattern = new Regex(
            @"id=""comment-(\d+)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );
        var matches = commentPattern.Matches(html);

        foreach (Match match in matches)
        {
            if (match.Success && match.Groups.Count > 1)
            {
                commentIds.Add(match.Groups[1].Value);
            }
        }

        return commentIds;
    }

    public static DateTimeOffset? ParseJiraDate(string? dateText)
    {
        if (string.IsNullOrEmpty(dateText))
            return null;

        // Common Jira date formats
        var formats = new[]
        {
            "dd/MMM/yyyy HH:mm",
            "dd/MMM/yyyy",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss.fffZ",
            "yyyy-MM-ddTHH:mm:ssZ",
        };

        foreach (var format in formats)
        {
            if (
                DateTimeOffset.TryParseExact(
                    dateText,
                    format,
                    null,
                    System.Globalization.DateTimeStyles.None,
                    out var result
                )
            )
            {
                return result;
            }
        }

        // Fallback to general parsing
        if (DateTimeOffset.TryParse(dateText, out var fallbackResult))
        {
            return fallbackResult;
        }

        return null;
    }
}
