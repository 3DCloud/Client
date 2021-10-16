using System;
using System.Threading;
using System.Threading.Tasks;
using ActionCableSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Print3DCloud.Client.Configuration;

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

            using IHost host = CreateHostBuilder(args, config).Build();
            IServiceProvider services = host.Services;

            ILogger<Program> logger = services.GetRequiredService<ILogger<Program>>();

            if (string.IsNullOrWhiteSpace(config.ServerHost))
            {
                logger.LogError($"Server host is empty; shutting down");
                return;
            }

            await services.GetRequiredService<DeviceManager>().StartAsync(CancellationToken.None).ConfigureAwait(false);
            await host.RunAsync();
            await config.SaveAsync(CancellationToken.None);
        }

        private static IHostBuilder CreateHostBuilder(string[] args, Config config)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                {
                    services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddConsole());

                    services.AddSingleton((serviceProvider) =>
                    {
                        Config resolvedConfig = serviceProvider.GetRequiredService<Config>();
                        ActionCableClient actionCableClient = new(serviceProvider.GetRequiredService<ILogger<ActionCableClient>>(), new Uri($"ws://{resolvedConfig.ServerHost}/cable"), "3DCloud-Client");

                        actionCableClient.AdditionalHeaders.Add(("X-Client-Id", resolvedConfig.ClientId.ToString()));
                        actionCableClient.AdditionalHeaders.Add(("X-Client-Secret", resolvedConfig.Secret));

                        return actionCableClient;
                    });

                    services.AddSingleton(config);
                    services.AddSingleton<DeviceManager>();
                });
        }
    }
}
