using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;

namespace DataCollection.Utils;

public static class BuilderHelpers
{
    public static bool IsNonEmpty(this IConfigurationSection section) =>
        section.Value != null || section.GetChildren().Any();

    [RequiresDynamicCode("")]
    [RequiresUnreferencedCode("")]
    public static OptionsBuilder<TOptions> AddOptionsFromOwnSectionAndValidateOnStart<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TOptions
    >(this IServiceCollection services, IConfiguration configuration)
        where TOptions : class =>
        services
            .AddOptionsWithValidateOnStart<TOptions>()
            .Bind(
                new string[]
                {
                    typeof(TOptions).Name,
                    typeof(TOptions).Name.RemoveSuffix("Options"),
                }
                    .Select(configuration.GetSection)
                    .Where(IsNonEmpty)
                    .First()
            );

    public static TOptions GetOptions<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
            TOptions
    >(this IServiceProvider sp) => sp.GetRequiredService<IOptionsMonitor<TOptions>>().CurrentValue;

    /// <summary>
    /// Get <typeparamref name="TOptions"/> from <see cref="IConfigurationManager"/> if specified,
    /// otherwise the default value.
    /// <br/>
    /// Use only if app hasn't been built yet. Otherwise, use <see cref="GetOptions{TOptions}(IServiceProvider)"/>.
    /// </summary>
    [RequiresUnreferencedCode("")]
    [RequiresDynamicCode("")]
    public static TOptions GetOptions<TOptions>(this IConfigurationManager cm)
        where TOptions : new() =>
        cm.GetSection(typeof(TOptions).Name).Exists()
            ? cm.GetRequiredSection(typeof(TOptions).Name).Get<TOptions>()!
            : new TOptions();

    public static ILoggingBuilder UseDebugSerilog(
        this ILoggingBuilder lb,
        IConfiguration configs
    ) =>
        lb.AddSerilog(
            (Serilog.Core.Logger?)
                new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.Console()
                    .ReadFrom.Configuration(configs)
                    .Enrich.WithMachineName()
                    .Enrich.WithEnvironmentName()
                    .CreateLogger()
        );
}
