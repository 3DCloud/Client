using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ActionCableSharp;
using Microsoft.Extensions.Logging;
using Print3DCloud.Client.ActionCable;
using Print3DCloud.Client.Configuration;
using Print3DCloud.Client.Printers;

namespace Print3DCloud.Client
{
    /// <summary>
    /// Contains the initial logic that runs the program.
    /// </summary>
    internal class Program
    {
        private static ILogger<Program>? logger;
        private static Config? config;

        /// <summary>
        /// This is the program's entry point. It is the first thing that runs when the program is started.
        ///
        /// This currently contains a whole bunch of testing nonsense.
        /// </summary>
        /// <param name="args">Command-line arguments, including the name of the program.</param>
        /// <returns>A <see cref="Task"/>.</returns>
        private static async Task Main(string[] args)
        {
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole().SetMinimumLevel(LogLevel.Trace);
            });

            Logging.LoggerFactory = loggerFactory;
            ActionCableSharp.Logging.LoggerFactory = loggerFactory;

            logger = loggerFactory.CreateLogger<Program>();

            config = await Config.LoadAsync(CancellationToken.None);
            await config.SaveAsync(CancellationToken.None);

            using var printer = new MarlinPrinter(args[0]);

            await printer.ConnectAsync(CancellationToken.None);

            await printer.SendCommandAsync("G28 X Y", CancellationToken.None);
            await printer.SendCommandAsync("M18", CancellationToken.None);

            var cts = new CancellationTokenSource();

            Task sendTask = SendTask(printer, cts.Token);

            Task printTask = printer.StartPrintAsync(File.OpenRead(Path.Join(Directory.GetCurrentDirectory(), args[1])), cts.Token);

            string? input = null;

            do
            {
                input = Console.ReadLine();

                if (!string.IsNullOrEmpty(input))
                {
                    await printer.SendCommandAsync(input, CancellationToken.None);
                }
            }
            while (input != "exit");

            cts.Cancel();

            if (!printTask.IsCompleted)
            {
                try
                {
                    logger.LogInformation("Waiting for print to finish...");
                    await printTask;
                }
                catch (OperationCanceledException)
                {
                    logger.LogInformation("Print task canceled");
                    await printer.SendCommandAsync("M104 T0 S0", CancellationToken.None);
                    await printer.SendCommandAsync("M104 T1 S0", CancellationToken.None);
                    await printer.SendCommandAsync("M140 S0", CancellationToken.None);
                    await printer.SendCommandAsync("G91", CancellationToken.None);
                    await printer.SendCommandAsync("G0 E-1 F300", CancellationToken.None);
                    await printer.SendCommandAsync("G0 Z5", CancellationToken.None);
                    await printer.SendCommandAsync("G28 X Y", CancellationToken.None);
                    await printer.SendCommandAsync("M18", CancellationToken.None);
                }
            }

            await printer.DisconnectAsync();
            await sendTask;
        }

        private static async Task SendTask(IPrinter printer, CancellationToken cancellationToken)
        {
            if (config == null) return;

            using var client = new ActionCableClient(new Uri("ws://localhost:3000/ws/"), "3DCloud-Client");

            await client.ConnectAsync(cancellationToken);

            var obj = new ClientMessageReceiver(config);
            ActionCableSubscription subscription = await client.Subscribe(new ClientIdentifier(config.Guid, config.Key), obj, CancellationToken.None);

            while (!cancellationToken.IsCancellationRequested)
            {
                if (client.State == ClientState.Connected && subscription?.State == SubscriptionState.Subscribed)
                {
                    await subscription.Perform(new PrinterStateMessage(new Dictionary<string, PrinterState> { { printer.Identifier, printer.State } }), CancellationToken.None);
                }

                await Task.Delay(1000, cancellationToken);
            }

            await client.DisconnectAsync(CancellationToken.None);
        }
    }
}
