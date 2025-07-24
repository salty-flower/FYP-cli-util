using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.RateLimiting;
using DataCollection.Application.Common.Services;
using DataCollection.Application.Features.BugDiscovery;
using DataCollection.Application.Features.BugDiscovery.Caching;
using DataCollection.Application.Features.IssueAnalysis.Rules;
using DataCollection.Application.Features.IssueProcessing;
using DataCollection.Application.Features.PaperAnalysis;
using DataCollection.Application.Features.PatternMatching;
using DataCollection.Application.Features.SemanticAgents;
using DataCollection.Application.Features.SemanticAgents.Tools;
using DataCollection.Application.Options;
using DataCollection.Infrastructure.Clients;
using DataCollection.Infrastructure.Clients.ACM;
using DataCollection.Infrastructure.Clients.Handlers;
using DataCollection.Infrastructure.Clients.IssueTrackers;
using DataCollection.Infrastructure.Clients.WebSearch;
using DataCollection.Infrastructure.Options;
using DataCollection.Infrastructure.Persistence;
using DataCollection.Presentation.Cli.Commands;
using DataCollection.Presentation.Cli.Commands.Repl;
using DataCollection.Presentation.Cli.Rendering;
using GitHub.Octokit.Client;
using GitHub.Octokit.Client.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI;
using Refit;
using GitHubClient = DataCollection.Infrastructure.Clients.IssueTrackers.GitHubClient;

namespace DataCollection.Presentation.Cli.Setup;

public static class ServiceRegistrationExtensions
{
    public static IServiceCollection AddHttpClients(
        this IServiceCollection services,
        IConfiguration config
    )
    {
        services.AddHttpClient(
            "OpenAIBatchApi",
            (sp, client) =>
            {
                var credentialOptions = sp.GetOptions<CredentialOptions>();
                client.DefaultRequestHeaders.Add(
                    "Authorization",
                    $"Bearer {credentialOptions.OpenAIToken}"
                );
                client.DefaultRequestHeaders.Add("User-Agent", "OpenAI-DotNet-Batch");
            }
        );

        services
            .AddHttpClient(
                "acm-scraper",
                (sp, client) =>
                {
                    var scraperConfig = sp.GetRequiredService<
                        IOptionsSnapshot<ScraperOptions>
                    >().Value;
                    client.BaseAddress = new Uri(scraperConfig.AcmBaseUrl);
                    foreach (var cookie in scraperConfig.Cookies)
                    {
                        client.DefaultRequestHeaders.TryAddWithoutValidation(
                            "Cookie",
                            $"{cookie.Key}={cookie.Value}"
                        );
                    }
                }
            )
            .ConfigurePrimaryHttpMessageHandler(() =>
                new ClientSideRateLimitedHandler(
                    new SlidingWindowRateLimiter(
                        new SlidingWindowRateLimiterOptions
                        {
                            QueueLimit = int.MaxValue,
                            Window = TimeSpan.FromSeconds(6),
                            PermitLimit = 2,
                            SegmentsPerWindow = 1,
                        }
                    )
                )
            );

        services
            .AddHttpClient(
                "ddg",
                client =>
                {
                    client.BaseAddress = new Uri("https://duckduckgo.com/");
                    client.DefaultRequestHeaders.Add(
                        "User-Agent",
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/115.0"
                    );
                    client.DefaultRequestHeaders.Add(
                        "Accept",
                        "application/json, text/javascript, */*; q=0.01"
                    );
                }
            )
            .ConfigurePrimaryHttpMessageHandler(() =>
                new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }
            );

        return services;
    }

    public static IServiceCollection AddGitHubServices(
        this IServiceCollection services,
        IConfiguration config
    )
    {
        services.AddSingleton<TokenProvider>(sp => new TokenProvider(
            sp.GetOptions<CredentialOptions>().GitHubToken
        ));
        services.AddSingleton<GitHub.GitHubClient>(sp => new GitHub.GitHubClient(
            RequestAdapter.Create(new TokenAuthProvider(sp.GetRequiredService<TokenProvider>()))
        ));
        services
            .AddHttpClient(
                "github-api",
                (sp, client) =>
                {
                    var credentialOptions = sp.GetOptions<CredentialOptions>();
                    client.BaseAddress = new Uri("https://api.github.com/");
                    client.DefaultRequestHeaders.Accept.Add(
                        new MediaTypeWithQualityHeaderValue("application/vnd.github+json")
                    );
                    client.DefaultRequestHeaders.Add("User-Agent", "BugMiner/1.0");
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                        "Bearer",
                        credentialOptions.GitHubToken
                    );
                }
            )
            .ConfigurePrimaryHttpMessageHandler(() =>
                new HttpClientHandler
                {
                    AutomaticDecompression =
                        DecompressionMethods.GZip | DecompressionMethods.Deflate,
                }
            );

        services
            .AddRefitClient<IGitHubApi>()
            .ConfigureHttpClient(
                (sp, client) =>
                {
                    var credentialOptions = sp.GetOptions<CredentialOptions>();
                    client.BaseAddress = new Uri("https://api.github.com/");
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                        "Bearer",
                        credentialOptions.GitHubToken
                    );
                }
            );

        return services;
    }

    public static IServiceCollection AddDiscoveryServices(this IServiceCollection services)
    {
        services.AddSingleton<IPatternMatchingService, PatternMatchingService>();
        services.AddSingleton<IPatternConfigurationService, PatternConfigurationService>();
        services.AddSingleton<BugListDiscoveryService>();
        services.AddSingleton<PdfContentAnalysisService>();
        services.AddSingleton<RepositoryAnalysisService>();
        services.AddSingleton<WebSearchAnalysisService>();
        services.AddSingleton<AcmScraper>();
        services.AddSingleton<LlmPromptService>();
        services.AddSingleton<ValidationService>();
        services.AddSingleton<UrlProcessingService>();
        services.AddSingleton<AcmPaperParser>();
        services.AddSingleton<AcmPaperDownloader>();
        services.AddSingleton<PaperEnricher>();
        services.AddSingleton<PdfDescriptionService>();
        services.AddSingleton<IGitHubClient, GitHubClient>();
        services.AddSingleton<IWebSearchService, DuckDuckGoSearchService>();
        services.AddSingleton<IRepositoryCache, RepositoryCache>();
        services.AddSingleton<ConsoleRenderingService>();
        services.AddSingleton<PdfSearchService>();
        services.AddScoped<DatabaseDataLoadingService>();
        services.AddScoped<DatabaseIssueAnalysisStorageService>();
        services.AddScoped<DatabaseBugListDiscoveryStorageService>();
        services.AddSingleton<GitHubService>();
        services.AddSingleton<SingleIssueProcessingService>();
        services.AddSingleton<IssueBatchProcessingService>();
        services.AddSingleton<IsDeveloperCriterion>();
        services.AddSingleton<IssueOverallStatusCriterion>();
        services.AddScoped<BatchFileHandler>();
        services.AddScoped<BatchJobPoller>();
        services.AddScoped<IUserProfileCache, UserProfileCache>();
        services.AddSingleton<IssueFixedCriterion>();
        services.AddSingleton<ReplCommands>();
        services.AddSingleton<TextLinesReplCommand>();
        services.AddSingleton<PdfReplCommand>();
        services.AddSingleton<MetadataReplCommand>();
        services.AddSingleton<ProcedureCommands>();
        services.AddSingleton<IssueCommands>();
        services.AddSingleton<BugListDiscoveryCommands>();
        services.AddSingleton<SeedCommand>();
        services.AddSingleton<OpenAIClient>(sp => new OpenAIClient(
            sp.GetOptions<CredentialOptions>().OpenAIToken
        ));
        services.AddScoped<IChatCompletionService, OpenAIChatCompletionService>(sp =>
            new("o4-mini", sp.GetOptions<CredentialOptions>().OpenAIToken)
        );
        services.AddSingleton<IIssueTrackerClientFactory, IssueTrackerClientFactory>();
        services.AddScoped<DiscoveryTools>();
        services.AddScoped<IDiscoveryAgent, DiscoveryAgent>();
        services.AddScoped<IDiscoveryAgentService, DiscoveryAgentService>();
        return services;
    }

    [RequiresUnreferencedCode("Options configuration uses reflection.")]
    public static IServiceCollection AddOptions(
        this IServiceCollection services,
        IConfiguration config
    )
    {
        services.AddOptionsFromRootAndValidateOnStart<RootOptions>(config);
        services.AddOptionsFromOwnSectionAndValidateOnStart<ScraperOptions>(config);
        services.AddOptionsFromOwnSectionAndValidateOnStart<PathsOptions>(config);
        services.AddOptionsFromOwnSectionAndValidateOnStart<ParallelismOptions>(
            config,
            allowDefault: true
        );
        services.AddOptionsFromOwnSectionAndValidateOnStart<CredentialOptions>(
            config,
            allowDefault: true
        );
        services.AddOptionsFromOwnSectionAndValidateOnStart<LLMOptions>(config, allowDefault: true);

        // Configure discovery-specific options
        services.AddOptionsFromOwnSectionAndValidateOnStart<ThresholdOptions>(
            config,
            allowDefault: true
        );
        services.AddOptionsFromOwnSectionAndValidateOnStart<LimitOptions>(
            config,
            allowDefault: true
        );
        services.AddOptionsFromOwnSectionAndValidateOnStart<KeywordOptions>(
            config,
            allowDefault: true
        );
        services.AddOptionsFromOwnSectionAndValidateOnStart<FileNameOptions>(
            config,
            allowDefault: true
        );
        services.AddOptionsFromOwnSectionAndValidateOnStart<StopWordOptions>(
            config,
            allowDefault: true
        );
        services.AddOptionsFromOwnSectionAndValidateOnStart<KnownHostOptions>(
            config,
            allowDefault: true
        );
        services.AddOptionsFromOwnSectionAndValidateOnStart<ArtifactSectionOptions>(
            config,
            allowDefault: true
        );

        return services;
    }

    public static IServiceCollection AddDatabase(
        this IServiceCollection services,
        IConfiguration config
    )
    {
        services.AddDbContext<DataCollectionDbContext>(options =>
            options.UseSqlite(
                $"Data Source={Path.Combine(services.BuildServiceProvider().GetOptions<PathsOptions>().BaseDir, "data-collection.db")}"
            )
        );
        return services;
    }

    public static IServiceCollection AddSemanticKernel(this IServiceCollection services)
    {
        services.AddScoped<Kernel>(sp =>
        {
            var llmOptions = sp.GetOptions<LLMOptions>();
            var openAIClient = sp.GetRequiredService<OpenAIClient>();
            var discoveryTools = sp.GetRequiredService<DiscoveryTools>();

            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.AddOpenAIChatCompletion(llmOptions.AgentPlanningModel, openAIClient);

            // Copy all required services from the main DI container to the kernel's DI container
            kernelBuilder.Services.AddSingleton(openAIClient);
            kernelBuilder.Services.AddSingleton(
                sp.GetRequiredService<IOptionsSnapshot<KeywordOptions>>()
            );
            kernelBuilder.Services.AddSingleton(sp.GetRequiredService<IGitHubClient>());
            kernelBuilder.Services.AddSingleton(sp.GetRequiredService<IWebSearchService>());
            kernelBuilder.Services.AddSingleton(sp.GetRequiredService<ILoggerFactory>());

            var kernel = kernelBuilder.Build();

            // Register DiscoveryTools as a plugin using instance-based approach
            var plugin = kernel.Plugins.AddFromObject(discoveryTools, "DiscoveryTools");

            // Log plugin registration for debugging
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("SemanticKernel");
            logger.LogInformation(
                "Registered DiscoveryTools plugin with {FunctionCount} functions",
                plugin.FunctionCount
            );

            return kernel;
        });
        return services;
    }
}
