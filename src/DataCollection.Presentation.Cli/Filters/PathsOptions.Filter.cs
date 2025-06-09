using ConsoleAppFramework;
using DataCollection.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace DataCollection.Presentation.Cli.Filters;

internal class PathsOptionsFilter(
    ConsoleAppFilter next,
    IOptionsSnapshot<PathsOptions> pathsOptions
) : ConsoleAppFilter(next)
{
    public override async Task InvokeAsync(
        ConsoleAppContext context,
        CancellationToken cancellationToken
    )
    {
        pathsOptions.Value.EnsureDirectoriesExist();
        await Next.InvokeAsync(context, cancellationToken);
    }
}
