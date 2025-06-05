using ConsoleAppFramework;
using DataCollection.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Presentation.Cli.Filters;

internal class CredentialOptionsFilter(
    ConsoleAppFilter next,
    IOptionsSnapshot<CredentialOptions> credentials,
    ILogger<CredentialOptionsFilter> logger
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
            logger.LogError("GitHub token or OpenAI token is not set: {value}", maybeCredentials);
            Environment.Exit(1);
            return;
        }

        await Next.InvokeAsync(context, cancellationToken);
    }
}
