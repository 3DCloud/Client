﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ActionCableSharp;
using Microsoft.Extensions.Logging;

namespace Client
{
    /// <summary>
    /// Contains the initial logic that runs the program.
    /// </summary>
    internal class Program
    {
        private static readonly Random Random = new Random();
        private static ILogger<Program>? logger;

        private static async Task Main(string[] args)
        {
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole().SetMinimumLevel(LogLevel.Trace);
            });

            Logging.LoggerFactory = loggerFactory;
            ActionCableSharp.Logging.LoggerFactory = loggerFactory;

            logger = loggerFactory.CreateLogger<Program>();

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
