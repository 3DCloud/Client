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
            var config = await Config.LoadAsync(CancellationToken.None);

            using IHost host = CreateHostBuilder(args, config).Build();
            IServiceProvider services = host.Services;

            await services.GetRequiredService<ActionCableClient>().ConnectAsync(CancellationToken.None);
            await services.GetRequiredService<DeviceManager>().StartAsync();

            await host.RunAsync();
        }

        private static IHostBuilder CreateHostBuilder(string[] args, Config config)
        {

            return Host.CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                {
                    services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddConsole());

                    services.AddSingleton((serviceProvider) => new ActionCableClient(serviceProvider.GetRequiredService<ILoggerFactory>(), new Uri("ws://localhost:3000/cable"), "3DCloud-Client"));
                    services.AddSingleton(config);
                    services.AddSingleton<DeviceManager>();
                });
        }
    }
}
