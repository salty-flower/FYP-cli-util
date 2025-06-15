using System.Net;
using System.Net.Http.Headers;
using System.Threading.RateLimiting;
using ConsoleAppFramework;
using DataCollection.Application.Features.BugDiscovery;
using DataCollection.Application.Features.Configuration;
using DataCollection.Application.Features.IssueAnalysis.Rules;
using DataCollection.Application.Features.IssueProcessing;
using DataCollection.Application.Features.PaperAnalysis;
using DataCollection.Application.Features.PatternMatching;
using DataCollection.Application.Features.SemanticAgents;
using DataCollection.Application.Features.SemanticAgents.Tools;
using DataCollection.Core.Models.IssueTracker;
using DataCollection.Infrastructure.Clients;
using DataCollection.Infrastructure.Clients.Handlers;
using DataCollection.Infrastructure.Clients.IssueTrackers;
using DataCollection.Infrastructure.Options;
using DataCollection.Infrastructure.Persistence;
using DataCollection.Presentation.Cli.Commands;
using DataCollection.Presentation.Cli.Commands.Repl;
using DataCollection.Presentation.Cli.Filters;
using DataCollection.Presentation.Cli.Rendering;
using DataCollection.Presentation.Cli.Setup;
using GitHub;
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
using Serilog;
using Serilog.Settings.Configuration;

var options = new ConfigurationReaderOptions(typeof(ConsoleLoggerConfigurationExtensions).Assembly);
var builder = ConsoleApp
    .Create()
    .ConfigureDefaultConfiguration(cfg =>
        cfg.AddEnvironmentVariables().AddJsonFile("appsettings.local.json")
    );

builder.ConfigureLogging(
    (config, logging) =>
    {
        logging.ClearProviders();
        logging.AddSerilog(
            new LoggerConfiguration().ReadFrom.Configuration(config, options).CreateLogger()
        );
    }
);

builder.UseFilter<RootOptionsFilter>();

var app = builder.ConfigureServices(
    (config, services) =>
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

        // Configure Entity Framework
        services.AddDbContext<DataCollectionDbContext>(options =>
        {
            var pathsOptions = services
                .BuildServiceProvider()
                .GetRequiredService<IOptions<PathsOptions>>()
                .Value;
            var dbPath = Path.Combine(pathsOptions.BaseDir, "data-collection.db");
            options.UseSqlite($"Data Source={dbPath}");
        });

        services
            .AddHttpClient(
                "acm-scraper",
                (sp, client) =>
                {
                    var config = sp.GetRequiredService<IOptionsSnapshot<ScraperOptions>>().Value;
                    var baseUrl = config.AcmBaseUrl;
                    client.BaseAddress = new Uri(baseUrl);

                    // Add cookies from configuration
                    var cookies = config.Cookies;
                    if (cookies.Count == 0)
                        throw new InvalidOperationException("No cookies found in configuration");

                    foreach (var cookie in cookies)
                        client.DefaultRequestHeaders.TryAddWithoutValidation(
                            "Cookie",
                            $"{cookie.Key}={cookie.Value}"
                        );
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

        services.UseMinimalHttpLogger();

        // Register new GitHub SDK components
        services.AddSingleton<TokenProvider>(sp => new TokenProvider(
            sp.GetOptions<CredentialOptions>().GitHubToken
        ));
        services.AddSingleton<GitHub.GitHubClient>(sp =>
        {
            var tokenProvider = sp.GetRequiredService<TokenProvider>();
            var adapter = RequestAdapter.Create(new TokenAuthProvider(tokenProvider));
            return new GitHub.GitHubClient(adapter);
        });

        services.AddSingleton(sp => new OpenAIClient(
            sp.GetOptions<CredentialOptions>().OpenAIToken
        ));

        services.AddSingleton<Kernel>(sp =>
        {
            var credentialOptions = sp.GetRequiredService<IOptions<CredentialOptions>>().Value;
            var openAIClient = sp.GetRequiredService<OpenAIClient>();

            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.AddOpenAIChatCompletion(
                credentialOptions.BigModel,
                credentialOptions.OpenAIToken
            );

            // Register both big and small model chat clients
            kernelBuilder.Services.AddSingleton(openAIClient);
            kernelBuilder.Services.AddSingleton(
                openAIClient.GetChatClient(credentialOptions.BigModel)
            );
            kernelBuilder.Services.AddSingleton(
                openAIClient.GetChatClient(credentialOptions.SmallModel)
            );

            var kernel = kernelBuilder.Build();

            // Register discovery tools as a plugin
            // Note: DiscoveryTools will be created by the DI container when needed
            // This is commented out to avoid build issues, but shows how to register plugins
            // var discoveryTools = sp.GetRequiredService<DiscoveryTools>();
            // kernel.Plugins.AddFromObject(discoveryTools, "DiscoveryTools");

            return kernel;
        });

        // Register web search services
        services
            .AddHttpClient<DuckDuckGoSearchService>(client =>
            {
                client.DefaultRequestHeaders.Add(
                    "User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/115.0"
                );
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
                new HttpClientHandler()
                {
                    AutomaticDecompression = System.Net.DecompressionMethods.All,
                }
            );

        services.AddSingleton<IWebSearchService, DuckDuckGoSearchService>();

        // Register bug list discovery services
        services.AddTransient(sp =>
            sp.GetRequiredService<OpenAIClient>()
                .GetChatClient(sp.GetOptions<LLMOptions>().IssueOverallStatusModel)
        );
        services.AddSingleton<IPatternMatchingService, PatternMatchingService>();
        services.AddSingleton<IPatternConfigurationService, PatternConfigurationService>();
        services.AddSingleton<BugListDiscoveryService>();
        services.AddSingleton<PdfContentAnalysisService>();
        services.AddSingleton<RepositoryAnalysisService>();
        services.AddSingleton<WebSearchAnalysisService>();

        services.AddSingleton<AcmScraper>();
        services.AddSingleton<AcmPaperParser>();
        services.AddSingleton<AcmPaperDownloader>();
        services.AddSingleton<PdfDescriptionService>();
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
        services.AddSingleton<IssueFixedCriterion>();
        services.AddSingleton<ReplCommands>();
        services.AddSingleton<TextLinesReplCommand>();
        services.AddSingleton<PdfReplCommand>();
        services.AddSingleton<MetadataReplCommand>();
        services.AddSingleton<ProcedureCommands>();
        services.AddSingleton<IssueCommands>();
        services.AddSingleton<BugListDiscoveryCommands>();
        services.AddSingleton<SeedCommand>();

        // Register refactored command classes
        services.AddRefactoredBugListDiscovery(config);
        services.AddRefactoredIssueProcessing();
        services.AddRefactoredProcedureAnalysis(config);
        services.AddRefactoredBatchProcessing();
        services.AddRefactoredIssueAnalysis(config);

        // Register Configuration Service
        services.AddScoped<IConfigurationService, ConfigurationService>();
        services.AddScoped<IChatCompletionService, OpenAIChatCompletionService>(sp =>
            new(modelId: "o4-mini", apiKey: sp.GetOptions<CredentialOptions>().OpenAIToken)
        );

        // Register Issue Tracker Services
        services.AddSingleton<IIssueTrackerClientFactory, IssueTrackerClientFactory>();
        services.AddRefactoredPdfAnalysis(config);
        services.AddRefactoredPatternMatching(config);

        services.AddScoped<TextProcessor>();
        services.AddScoped<PaperAnalyzer>();

        // Register SemanticKernel Agent Services
        // Note: These services enable AI-powered discovery agents for bug lists and artifacts
        services.AddScoped<DiscoveryTools>();
        services.AddScoped<IDiscoveryAgent, DiscoveryAgent>();
        services.AddScoped<IDiscoveryAgentService, DiscoveryAgentService>();

        // TODO: When SemanticKernel is fully enabled, uncomment the plugin registration in Kernel setup above
        // Add GitHub API HttpClient with token
        services
            .AddHttpClient(
                "github-api",
                (sp, client) =>
                {
                    var credentialOptions = sp.GetRequiredService<
                        IOptions<CredentialOptions>
                    >().Value;
                    client.BaseAddress = new Uri("https://api.github.com/");
                    client.DefaultRequestHeaders.Accept.Add(
                        new MediaTypeWithQualityHeaderValue("text/html")
                    );
                    client.DefaultRequestHeaders.Accept.Add(
                        new MediaTypeWithQualityHeaderValue("application/xhtml+xml")
                    );
                    client.DefaultRequestHeaders.Accept.Add(
                        new MediaTypeWithQualityHeaderValue("application/xml")
                    );
                    client.DefaultRequestHeaders.Accept.Add(
                        new MediaTypeWithQualityHeaderValue("application/json")
                    );
                    client.DefaultRequestHeaders.Accept.Add(
                        new MediaTypeWithQualityHeaderValue("application/vnd.github+json")
                    );
                    client.DefaultRequestHeaders.AcceptEncoding.Add(
                        new StringWithQualityHeaderValue("gzip")
                    );
                    client.DefaultRequestHeaders.AcceptEncoding.Add(
                        new StringWithQualityHeaderValue("deflate")
                    );
                    client.DefaultRequestHeaders.Add("User-Agent", "BugMiner/1.0");
                    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
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

        services.AddSingleton<DataCollection.Infrastructure.Clients.GitHubClient>();
        services
            .AddRefitClient<IGitHubApi>()
            .ConfigureHttpClient(
                (sp, client) =>
                {
                    var credentialOptions = sp.GetRequiredService<
                        IOptions<CredentialOptions>
                    >().Value;
                    client.BaseAddress = new Uri("https://api.github.com/");
                    client.DefaultRequestHeaders.Accept.Add(
                        new MediaTypeWithQualityHeaderValue("application/json")
                    );
                    client.DefaultRequestHeaders.Accept.Add(
                        new MediaTypeWithQualityHeaderValue("application/vnd.github+json")
                    );
                    client.DefaultRequestHeaders.Add("User-Agent", "BugMiner/1.0");
                    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
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
    }
);

await app.RunAsync(args);
