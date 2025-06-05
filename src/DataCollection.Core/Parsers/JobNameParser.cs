using System.Text.RegularExpressions;

namespace DataCollection.Core.Parsers;

public static class JobNameParser
{
    private static readonly Regex JobNameRegex = new(@"^(.+)-(\d{4})$", RegexOptions.Compiled);

    public static (string Conf, int Year) ParseJobName(string jobName)
    {
        var match = JobNameRegex.Match(jobName);
        if (!match.Success)
        {
            throw new ArgumentException(
                $"Invalid job name format: {jobName}. Expected format: 'conf-yyyy'"
            );
        }

        var conf = match.Groups[1].Value;
        var year = int.Parse(match.Groups[2].Value);

        return (conf, year);
    }

    public static bool TryParseJobName(string jobName, out string conf, out int year)
    {
        try
        {
            (conf, year) = ParseJobName(jobName);
            return true;
        }
        catch
        {
            conf = string.Empty;
            year = 0;
            return false;
        }
    }
}
