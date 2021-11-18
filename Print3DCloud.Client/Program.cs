using System;
using System.Threading;
using System.Threading.Tasks;
using ActionCableSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Print3DCloud.Client.Configuration;
using Rollbar;
using Rollbar.PlugIns.Serilog;
using Serilog;

namespace Print3DCloud.Client
{
    /// <summary>
    /// Contains the initial logic that runs the program.
    /// </summary>
    internal class Program
    {
        /// <summary>
        /// This is the program's entry point. It is the first thing that runs when the program is started.
        /// </summary>
        /// <param name="args">Command-line arguments, including the name of the program.</param>
        /// <returns>A <see cref="Task"/>.</returns>
        private static async Task Main(string[] args)
        {
            Config config = await Config.LoadAsync(CancellationToken.None).ConfigureAwait(false);

            await config.SaveAsync(CancellationToken.None);

            using IHost host = CreateHostBuilder(args, config);
            IServiceProvider services = host.Services;

            ILogger<Program> logger = services.GetRequiredService<ILogger<Program>>();

            if (string.IsNullOrWhiteSpace(config.CablePath))
            {
                logger.LogError("Server host is empty; shutting down");
                return;
            }

            try
            {
                await host.RunAsync();
            }
            catch (Exception ex)
            {
                RollbarLocator.RollbarInstance.AsBlockingLogger(TimeSpan.FromMinutes(1)).Critical(ex);
            }

            logger.LogInformation("Shutting down");

            await config.SaveAsync(CancellationToken.None);

            logger.LogInformation("Shut down");
        }

        private static IHost CreateHostBuilder(string[] args, Config config)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureServices((services) => ConfigureServices(services, config))
                .UseSerilog((context, loggerConfiguration) => ConfigureSerilog(context, loggerConfiguration, config))
                .Build();
        }

        private static void ConfigureServices(IServiceCollection services, Config config)
        {
            services.AddSingleton((serviceProvider) =>
            {
                Config resolvedConfig = serviceProvider.GetRequiredService<Config>();
                ActionCableClient actionCableClient = new(serviceProvider.GetRequiredService<ILogger<ActionCableClient>>(), new Uri(resolvedConfig.CablePath), "3DCloud-Client");

                actionCableClient.AdditionalHeaders.Add(("X-Client-Id", resolvedConfig.ClientId.ToString()));
                actionCableClient.AdditionalHeaders.Add(("X-Client-Secret", resolvedConfig.Secret));

                return actionCableClient;
            });

            services.AddSingleton(config);
            services.AddSingleton<IHostedService, DeviceManager>();
        }

        private static void ConfigureSerilog(HostBuilderContext context, LoggerConfiguration loggerConfiguration, Config config)
        {
            loggerConfiguration
                .Enrich.FromLogContext()
                .MinimumLevel.Is(config.ConsoleLogLevel)
                .WriteTo.Console(config.ConsoleLogLevel);

            if (!string.IsNullOrEmpty(config.RollbarAccessToken))
            {
                RollbarConfig rollbarConfig = new()
                {
                    AccessToken = config.RollbarAccessToken,
                    Environment = context.HostingEnvironment.EnvironmentName,
                    Person = new Rollbar.DTOs.Person
                    {
                        Id = config.ClientId.ToString(),
                    },
                };

                loggerConfiguration
                    .WriteTo.RollbarSink(rollbarConfig, config.RollbarLogLevel);
            }
        }
    }
}
