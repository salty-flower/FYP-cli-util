using DataCollection.Application.Services;
using DataCollection.Infrastructure.Models.BugList;
using DataCollection.Infrastructure.Options;
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

        // Register pure application services
        services.AddScoped<IBugListDiscoveryService, PureBugListDiscoveryService>();

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
}
