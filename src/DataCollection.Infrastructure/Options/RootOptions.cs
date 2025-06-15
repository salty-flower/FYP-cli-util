using System.Text.RegularExpressions;

namespace DataCollection.Infrastructure.Options;

public class RootOptions
{
    /// <summary>
    /// Job name, typically in the format 'conf-yyyy', e.g., 'icse-2024', 'issta-2021'
    /// </summary>
    public required string JobName { get; init; }

    private JobName? parsedJobNameCache;

    public bool TryParseJobName(out JobName? jobName)
    {
        parsedJobNameCache ??= Options.JobName.TryParse(JobName);
        jobName = parsedJobNameCache;
        return parsedJobNameCache != null;
    }
}

public readonly partial struct JobName
{
    public string Conf { get; init; }
    public int Year { get; init; }

    [GeneratedRegex(@"^(.+)-(\d{4})$", RegexOptions.Compiled)]
    private static partial Regex JobNamePattern();

    public static JobName? TryParse(string jobName)
    {
        var match = JobNamePattern().Match(jobName);
        if (!match.Success)
            return null;

        return new JobName
        {
            Conf = match.Groups[1].Value,
            Year = int.Parse(match.Groups[2].Value),
        };
    }

    public override string ToString() => $"{Conf}-{Year}";
}
