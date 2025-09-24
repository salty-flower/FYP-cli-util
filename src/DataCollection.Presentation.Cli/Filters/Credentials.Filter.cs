using ConsoleAppFramework;
using DataCollection.Infrastructure.Clients.IssueTrackers;
using DataCollection.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Presentation.Cli.Filters;

internal class CredentialOptionsFilter(
    ConsoleAppFilter next,
    IOptionsSnapshot<CredentialOptions> credentials,
    IGitHubApi gitHubApi,
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

        // Verify GitHub authentication
        try
        {
            logger.LogInformation("Verifying GitHub authentication...");
            var userResponse = await gitHubApi.GetUserAsync();
            logger.LogInformation("GitHub authentication successful");
        }
        catch (Exception ex)
        {
            logger.LogError("GitHub authentication failed: {error}", ex.Message);
            Environment.Exit(1);
            return;
        }

        await Next.InvokeAsync(context, cancellationToken);
    }
}
