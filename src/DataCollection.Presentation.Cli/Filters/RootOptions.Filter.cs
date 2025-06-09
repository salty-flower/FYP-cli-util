using ConsoleAppFramework;
using DataCollection.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Presentation.Cli.Filters;

internal partial class RootOptionsFilter(
    ConsoleAppFilter next,
    IOptionsSnapshot<RootOptions> rootOptions,
    ILogger<RootOptionsFilter> logger
) : ConsoleAppFilter(next)
{
    private const string ErrorMessage =
        "Invalid job name format. Expected format: <conf>-<year>. Example: 'icse-2024'.";

    public override async Task InvokeAsync(
        ConsoleAppContext context,
        CancellationToken cancellationToken
    )
    {
        var isValid = rootOptions.Value.TryParseJobName(out var parsed);
        if (!isValid)
            throw new ArgumentException(ErrorMessage + $" Provided: {rootOptions.Value.JobName}");
        else
            logger.LogInformation("Parsed job name: {JobName}", parsed);

        await Next.InvokeAsync(context, cancellationToken);
    }
}
