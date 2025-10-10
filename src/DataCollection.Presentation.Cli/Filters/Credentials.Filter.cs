using ConsoleAppFramework;
using DataCollection.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Presentation.Cli.Filters;

internal class CredentialOptionsFilter(
    ConsoleAppFilter next,
    IOptionsSnapshot<CredentialOptions> credentials,
    IHttpClientFactory httpClientFactory,
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
            var client = httpClientFactory.CreateClient("github-api");
            using var response = await client.GetAsync("user", cancellationToken);
            response.EnsureSuccessStatusCode();
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
