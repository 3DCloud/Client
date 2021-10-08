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
            var config = await Config.LoadAsync(CancellationToken.None).ConfigureAwait(false);

            using IHost host = CreateHostBuilder(args, config).Build();
            IServiceProvider services = host.Services;

            ILogger<Program> logger = services.GetRequiredService<ILogger<Program>>();

            if (string.IsNullOrWhiteSpace(config.ServerHost))
            {
                logger.LogError($"Server host is empty; shutting down");
                return;
            }

            await services.GetRequiredService<ActionCableClient>().ConnectAsync(CancellationToken.None).ConfigureAwait(false);
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
                        var config = serviceProvider.GetRequiredService<Config>();
                        var actionCableClient = new ActionCableClient(serviceProvider.GetRequiredService<ILogger<ActionCableClient>>(), new Uri($"ws://{config.ServerHost}/cable"), "3DCloud-Client");

                        actionCableClient.AdditionalHeaders.Add(("X-Client-Id", config.ClientId.ToString()));
                        actionCableClient.AdditionalHeaders.Add(("X-Client-Secret", config.Secret));

                        return actionCableClient;
                    });

                    services.AddSingleton(config);
                    services.AddSingleton<DeviceManager>();
                });
        }
    }
}
