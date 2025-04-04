﻿using System;
using ConsoleAppFramework;
using DataCollection.Commands;
using DataCollection.Commands.Repl;
using DataCollection.Options;
using DataCollection.Services;
using DataCollection.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

builder.UseFilter<RootOptions.Filter>();

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

        services.AddHttpClient(
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
        );

        services.UseMinimalHttpLogger();
        services.AddSingleton<AcmScraper>();
        services.AddSingleton<PdfDescriptionService>();
        services.AddSingleton<ConsoleRenderingService>();
        services.AddSingleton<PdfSearchService>();
        services.AddSingleton<DataLoadingService>();
        services.AddSingleton<ReplCommands>();
        services.AddSingleton<TextLinesReplCommand>();
        services.AddSingleton<PdfReplCommand>();
        services.AddSingleton<MetadataReplCommand>();
        services.AddSingleton<ProcedureCommands>();
    }
);

await app.RunAsync(args);
