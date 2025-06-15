using ConsoleAppFramework;
using DataCollection.Infrastructure;
using DataCollection.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Python.Runtime;

namespace DataCollection.Presentation.Cli.Filters;

internal class PythonEngineInitFilter(
    ConsoleAppFilter next,
    IOptionsSnapshot<PathsOptions> pathOpt,
    ILogger<PythonEngineInitFilter> logger
) : ConsoleAppFilter(next)
{
    public override async Task InvokeAsync(
        ConsoleAppContext context,
        CancellationToken cancellationToken
    )
    {
        // Pre-check
        var declaredDllPath = pathOpt.Value.PythonDLL;
        if (!Path.IsPathRooted(declaredDllPath))
            declaredDllPath = Path.GetFullPath(
                Path.Combine(BuildConstants.SolutionDirectory, declaredDllPath)
            );

        if (!File.Exists(declaredDllPath))
        {
            logger.LogError("Python DLL not found at {path}", declaredDllPath);
            return;
        }

        logger.LogInformation("Python DLL found at {path}", declaredDllPath);
        Runtime.PythonDLL = declaredDllPath;
        PythonEngine.Initialize();
        PythonEngine.BeginAllowThreads();
        logger.LogInformation("Python engine initialized successfully");

        await Next.InvokeAsync(context, cancellationToken);
    }
}
