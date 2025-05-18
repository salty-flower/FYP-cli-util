using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConsoleAppFramework;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Options;

public partial class CredentialOptions
{
    internal class Filter(
        ConsoleAppFilter next,
        IOptionsSnapshot<CredentialOptions> credentials,
        ILogger<Filter> logger
    ) : ConsoleAppFilter(next)
    {
        public override async Task InvokeAsync(
            ConsoleAppContext context,
            CancellationToken cancellationToken
        )
        {
            var maybeCredentials = credentials.Value;
            if (
                new[] { maybeCredentials.GitHubToken, maybeCredentials.OpenAIToken }.Any(
                    string.IsNullOrWhiteSpace
                )
            )
            {
                logger.LogError(
                    "GitHub token or OpenAI token is not set: {value}",
                    maybeCredentials
                );
                Environment.Exit(1);
                return;
            }

            await Next.InvokeAsync(context, cancellationToken);
        }
    }
}
