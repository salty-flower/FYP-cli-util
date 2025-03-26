using System.Threading;
using System.Threading.Tasks;
using ConsoleAppFramework;
using DataCollection.Options;
using Microsoft.Extensions.Options;
using Python.Runtime;

namespace DataCollection.Filters;

internal class PythonEngineInitFilter(ConsoleAppFilter next, IOptionsSnapshot<PathsOptions> pathOpt)
    : ConsoleAppFilter(next)
{
    public override async Task InvokeAsync(
        ConsoleAppContext context,
        CancellationToken cancellationToken
    )
    {
        Runtime.PythonDLL = pathOpt.Value.PythonDLL;
        PythonEngine.Initialize();
        PythonEngine.BeginAllowThreads();

        await Next.InvokeAsync(context, cancellationToken);
    }
}
