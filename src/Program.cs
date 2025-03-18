using System;
using ConsoleAppFramework;
using DataCollection.Options;
using DataCollection.Services;
using DataCollection.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Settings.Configuration;

var options = new ConfigurationReaderOptions(typeof(ConsoleLoggerConfigurationExtensions).Assembly);
var builder = ConsoleApp.Create().ConfigureDefaultConfiguration();

builder.ConfigureLogging(
    (config, logging) =>
    {
        logging.ClearProviders();
        logging.AddSerilog(
            new LoggerConfiguration().ReadFrom.Configuration(config, options).CreateLogger()
        );
    }
);

var app = builder.ConfigureServices(
    (config, services) =>
    {
        services.AddOptionsFromOwnSectionAndValidateOnStart<ScraperOptions>(config);
        services.AddOptionsFromOwnSectionAndValidateOnStart<PathsOptions>(config);
        services.AddOptionsFromOwnSectionAndValidateOnStart<KeywordsOptions>(config);

        services.AddHttpClient(
            "acm-scraper",
            (sp, client) =>
            {
                var config = sp.GetRequiredService<IOptionsSnapshot<ScraperOptions>>().Value;
                var baseUrl = config.AcmBaseUrl;
                client.BaseAddress = new Uri(baseUrl);

                // Add cookies from configuration
                foreach (var cookie in config.Cookies)
                    client.DefaultRequestHeaders.TryAddWithoutValidation(
                        "Cookie",
                        $"{cookie.Key}={cookie.Value}"
                    );
            }
        );

        services.AddSingleton<AcmScraper>();
    }
);

await app.RunAsync(args);
