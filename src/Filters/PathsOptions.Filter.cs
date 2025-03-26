using System.Threading;
using System.Threading.Tasks;
using ConsoleAppFramework;
using Microsoft.Extensions.Options;

namespace DataCollection.Options;

public partial class PathsOptions
{
    internal class Filter(ConsoleAppFilter next, IOptionsSnapshot<PathsOptions> pathsOptions)
        : ConsoleAppFilter(next)
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
}
