using DataCollection.Application.Features.PatternMatching;
using DataCollection.Application.Services;
using DataCollection.Core.Interfaces;
using DataCollection.Infrastructure.Models.BugList;
using DataCollection.Infrastructure.Options;
using DataCollection.Infrastructure.Services;
using DataCollection.Presentation.Cli.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DataCollection.Presentation.Cli.Setup;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRefactoredBugListDiscovery(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        // Add and validate configuration
        services
            .AddOptions<BugListDiscoveryOptions>()
            .Bind(configuration.GetSection(BugListDiscoveryOptions.SectionName))
            .ValidateOnStart();

        services
            .AddOptions<BugDiscoveryOptions>()
            .Bind(configuration.GetSection(BugDiscoveryOptions.SectionName))
            .ValidateOnStart();

        // Register pure application services
        services.AddScoped<IBugListDiscoveryService, PureBugListDiscoveryService>();
        services.AddScoped<
            IBugDiscoveryOrchestrationService,
            PureBugDiscoveryOrchestrationService
        >();

        // Register presentation services
        services.AddScoped<
            IResultRenderer<BugListDiscoveryAnalysis>,
            BugListDiscoveryResultRenderer
        >();
        services.AddScoped<IErrorRenderer, BugListDiscoveryErrorRenderer>();

        return services;
    }

    public static IServiceCollection AddRefactoredIssueProcessing(this IServiceCollection services)
    {
        // Register pure application services
        services.AddScoped<IIssueProcessingService, PureIssueProcessingService>();

        return services;
    }

    public static IServiceCollection AddRefactoredProcedureAnalysis(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        // Add and validate configuration
        services
            .AddOptions<ProcedureAnalysisOptions>()
            .Bind(configuration.GetSection(ProcedureAnalysisOptions.SectionName))
            .ValidateOnStart();

        // Register pure application services
        services.AddScoped<IProcedureAnalysisService, PureProcedureAnalysisService>();

        return services;
    }

    public static IServiceCollection AddRefactoredBatchProcessing(this IServiceCollection services)
    {
        // Register pure application services
        services.AddScoped<IBatchProcessingService, PureBatchProcessingService>();

        return services;
    }

    public static IServiceCollection AddRefactoredPdfAnalysis(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        // Add and validate configuration
        services
            .AddOptions<PdfAnalysisOptions>()
            .Bind(configuration.GetSection(PdfAnalysisOptions.SectionName))
            .ValidateOnStart();

        // Register pure application services
        services.AddScoped<IPdfAnalysisService, PurePdfAnalysisService>();

        return services;
    }

    public static IServiceCollection AddRefactoredPatternMatching(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services
            .AddOptions<PatternMatchingOptions>()
            .Bind(configuration.GetSection(PatternMatchingOptions.SectionName))
            .ValidateOnStart();

        services.AddMemoryCache();
        services.AddScoped<IRuleProvider, CachedRuleProvider>();
        services.AddScoped<IPatternMatchingService, PurePatternMatchingService>();

        return services;
    }

    public static IServiceCollection AddRefactoredIssueAnalysis(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services
            .AddOptions<LlmOptions>()
            .Bind(configuration.GetSection(LlmOptions.SectionName))
            .ValidateOnStart();

        services.AddHttpClient(
            "OpenAIBatchApi",
            client =>
            {
                client.BaseAddress = new Uri("https://api.openai.com/");
                // The API key is injected by the OpenAI SDK configuration
            }
        );

        services.AddScoped(typeof(ILargeLanguageModelService<,>), typeof(OpenAiLlmService<,>));

        return services;
    }
}
