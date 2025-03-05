using System;
using System.IO;
using ConsoleAppFramework;
using DataCollection;
using DataCollection.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

ConsoleApp.ConsoleAppBuilder builder = ConsoleApp.Create();

builder.ConfigureLogging(
    (config, logging) =>
    {
        logging.ClearProviders();
        logging.AddSerilog(
            new LoggerConfiguration()
                .WriteTo.Console()
                .ReadFrom.Configuration(config)
                .CreateLogger()
        );
    }
);

var app = builder.ConfigureServices(services =>
{
    // Add configuration
    services.AddSingleton<IConfiguration>(sp =>
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false);
        return builder.Build();
    });

    // Configure HTTP client
    services.AddHttpClient(
        "acm-scraper",
        (sp, client) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var baseUrl = config.GetValue<string>("Scraper:AcmBaseUrl");
            client.BaseAddress = new Uri(baseUrl);

            // Add cookies from configuration
            var cookiesSection = config.GetSection("Scraper:Cookies");
            foreach (var cookie in cookiesSection.GetChildren())
                client.DefaultRequestHeaders.TryAddWithoutValidation(
                    "Cookie",
                    $"{cookie.Key}={cookie.Value}"
                );
        }
    );

    // Register services
    services.AddSingleton<AcmScraper>();
    services.AddSingleton<PaperAnalyzer>();
});

// Run the application
await app.RunAsync(args);
