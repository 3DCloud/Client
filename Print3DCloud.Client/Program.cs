using System;
using System.Collections.Generic;
using System.IO.Ports;
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
                builder.AddConsole().SetMinimumLevel(LogLevel.Trace);
            });

            Logging.LoggerFactory = loggerFactory;
            ActionCableSharp.Logging.LoggerFactory = loggerFactory;

            logger = loggerFactory.CreateLogger<Program>();

            logger.LogInformation(string.Join(", ", SerialPort.GetPortNames()));

            using (var p = new MarlinPrinter("COM10", 115200))
            {
                await p.ConnectAsync(CancellationToken.None);
                await p.SendCommandAsync("G28 X Y");

                for (int i = 0; i < 10; i++)
                {
                    await p.SendCommandAsync($"G0 X{i * 10}");
                }

                await Task.Delay(10_000);
                logger.LogInformation(p.GetState().ToString());
            }

            using var webSocket = new ActionCableClient(new Uri("ws://localhost:3000/ws/"), "3DCloud-Client");

            await webSocket.ConnectAsync();

            IPrinter printer = new DummyPrinter();

            while (!Console.KeyAvailable)
            {
                ActionCableSubscription subscription = await webSocket.Subscribe(new ClientIdentifier(Guid.NewGuid(), GetRandomBytes()));
                subscription.MessageReceived += OnMessageReceived;

                for (int i = 0; i < 5; i++)
                {
                    await subscription.Perform(new PrinterStateMessage(new Dictionary<string, PrinterState> { { printer.Identifier, printer.GetState() } }));
                    await Task.Delay(500);
                }

                await subscription.Unsubscribe();
                await Task.Delay(5000);
            }

            await webSocket.DisconnectAsync();
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
