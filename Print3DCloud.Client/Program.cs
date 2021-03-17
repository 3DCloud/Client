using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ActionCableSharp;
using Microsoft.Extensions.Logging;
using Print3DCloud.Client.ActionCable;
using Print3DCloud.Client.Printers;

namespace Print3DCloud.Client
{
    /// <summary>
    /// Contains the initial logic that runs the program.
    /// </summary>
    internal class Program
    {
        private static readonly Random Random = new Random();
        private static ILogger<Program>? logger;

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
                builder.AddConsole().SetMinimumLevel(LogLevel.Debug);
            });

            Logging.LoggerFactory = loggerFactory;
            ActionCableSharp.Logging.LoggerFactory = loggerFactory;

            logger = loggerFactory.CreateLogger<Program>();

            using var printer = new MarlinPrinter(args[0], 115200);
            using var client = new ActionCableClient(new Uri("ws://localhost:3000/ws/"), "3DCloud-Client");

            await printer.ConnectAsync(CancellationToken.None);
            await client.ConnectAsync(CancellationToken.None);

            await printer.SendCommandAsync("G28 X Y");

            _ = printer.StartPrintAsync(File.OpenRead(Path.Combine(Directory.GetCurrentDirectory(), args[1])), CancellationToken.None);

            ActionCableSubscription subscription = await client.Subscribe(new ClientIdentifier(Guid.NewGuid(), GetRandomBytes()), CancellationToken.None);
            subscription.MessageReceived += OnMessageReceived;

            while (!Console.KeyAvailable)
            {
                if (client.State == ClientState.Connected)
                {
                    await subscription.Perform(new PrinterStateMessage(new Dictionary<string, PrinterState> { { printer.Identifier, printer.State } }), CancellationToken.None);
                }

                await Task.Delay(1000);
            }

            await client.DisconnectAsync(CancellationToken.None);
        }

        private static string GetRandomBytes()
        {
            byte[] buffer = new byte[16];
            Random.NextBytes(buffer);
            return Convert.ToHexString(buffer);
        }

        private static void OnMessageReceived(ActionCableMessage message)
        {
            logger?.LogInformation(message.AsObject<SampleMessage>()?.SampleProperty);
        }
    }
}
