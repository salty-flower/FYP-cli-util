using ConsoleAppFramework;
using DataCollection.Presentation.Cli.Filters;
using DataCollection.Presentation.Cli.Setup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(config, options)
            .CreateLogger();
        logging.ClearProviders();
        logging.AddSerilog();
    }
);

builder.UseFilter<RootOptionsFilter>();

var app = builder.ConfigureServices(
    (config, services) =>
    {
        services.AddOptions(config);
        services.AddDatabase(config);
        services.AddHttpClients(config);
        services.AddGitHubServices(config);
        services.AddDiscoveryServices();
    }
);

await app.RunAsync(args);
