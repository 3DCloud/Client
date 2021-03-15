using System;
using System.Collections.Generic;
using System.IO;
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

            using var printer = new MarlinPrinter("COM10", 115200);
            using var webSocket = new ActionCableClient(new Uri("ws://localhost:3000/ws/"), "3DCloud-Client");

            await printer.ConnectAsync(CancellationToken.None);
            await webSocket.ConnectAsync();

            await printer.SendCommandAsync("G28 X Y");

            _ = printer.StartPrintAsync(File.OpenRead(@"D:\Users\Nicolas\Desktop\lil benchy.gcode"));

            ActionCableSubscription subscription = await webSocket.Subscribe(new ClientIdentifier(Guid.NewGuid(), GetRandomBytes()));
            subscription.MessageReceived += OnMessageReceived;

            while (!Console.KeyAvailable)
            {
                await subscription.Perform(new PrinterStateMessage(new Dictionary<string, PrinterState> { { printer.Identifier, printer.GetState() } }));
                await Task.Delay(1000);
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
