using System;
using System.Threading;
using System.Threading.Tasks;
using ConsoleAppFramework;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Options;

public partial class RootOptions
{
    internal class Filter(
        ConsoleAppFilter next,
        IOptionsSnapshot<RootOptions> rootOptions,
        ILogger<Filter> logger
    ) : ConsoleAppFilter(next)
    {
        public override async Task InvokeAsync(
            ConsoleAppContext context,
            CancellationToken cancellationToken
        )
        {
            var maybeJobName = rootOptions.Value.JobName;
            if (string.IsNullOrWhiteSpace(maybeJobName))
            {
                logger.LogError(
                    "JobName must be set. Either as an environment variable or in JSON"
                );
                Environment.Exit(1);
                return;
            }
            logger.LogInformation("Current job: {JobName}", maybeJobName!);

            await Next.InvokeAsync(context, cancellationToken);
        }
    }
}
