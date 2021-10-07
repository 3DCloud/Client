﻿using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Print3DCloud.Client.Printers;
using Print3DCloud.Client.Printers.Marlin;
using Print3DCloud.Client.Tests.TestUtilities;
using Xunit;

namespace Print3DCloud.Client.Tests.Printers.Marlin
{
    public class MarlinPrinterTests
    {
        [Fact]
        public async Task ConnectAsync_WhenPrinterRespondsAsExpected_ConnectsSuccessfully()
        {
            SerialPrinterStreamSimulator sim = new();
            Mock<ISerialPort> serialPortStreamMock = CreateSerialPort("COM0", 125_000, sim);

            sim.RegisterResponse("N0 M110 N0*125", "ok");
            sim.RegisterResponse(new Regex(@"N1 M155 S\d+\*\d+"), "ok");

            Mock<ISerialPortFactory> serialPortStreamFactoryMock = new();
            serialPortStreamFactoryMock.Setup(f => f.CreatePrinterStream(It.IsAny<string>(), It.IsAny<int>())).Returns<string, int>((s, i) => serialPortStreamMock.Object);

            MarlinPrinter printer = new(serialPortStreamFactoryMock.Object, TestHelpers.CreateLogger<MarlinPrinter>().Object, "COM0", 125_000);

            Assert.Equal(PrinterState.Disconnected, printer.State);

            Task connectTask = printer.ConnectAsync(TestHelpers.CreateTimeOutToken());

            Assert.Equal(PrinterState.Connecting, printer.State);

            sim.SendMessage("start");

            await connectTask;

            serialPortStreamFactoryMock.Verify(f => f.CreatePrinterStream("COM0", 125_000));
            serialPortStreamMock.Verify(s => s.DiscardInBuffer());
            serialPortStreamMock.Verify(s => s.DiscardOutBuffer());
            serialPortStreamMock.Verify(s => s.Open());

            Assert.Equal(PrinterState.Ready, printer.State);
        }

        [Fact]
        public async Task ConnectAsync_WhenPrinterDoesNotSupportAutomaticTemperatureReporting_ConnectsSuccessfully()
        {
            SerialPrinterStreamSimulator sim = new();
            Mock<ISerialPort> serialPortStreamMock = CreateSerialPort("COM0", 125_000, sim);

            sim.RegisterResponse("N0 M110 N0*125", "ok");
            sim.RegisterResponse(new Regex(@"N1 (M155 S\d+)\*\d+"), "echo:Unknown command: \"$1\"\nok");

            Mock<ISerialPortFactory> serialPortStreamFactoryMock = new();
            serialPortStreamFactoryMock.Setup(f => f.CreatePrinterStream(It.IsAny<string>(), It.IsAny<int>())).Returns<string, int>((s, i) => serialPortStreamMock.Object);

            MarlinPrinter printer = new(serialPortStreamFactoryMock.Object, TestHelpers.CreateLogger<MarlinPrinter>().Object, "COM0", 125_000);

            Assert.Equal(PrinterState.Disconnected, printer.State);

            Task connectTask = printer.ConnectAsync(TestHelpers.CreateTimeOutToken());

            Assert.Equal(PrinterState.Connecting, printer.State);

            sim.SendMessage("start");

            await connectTask;

            serialPortStreamFactoryMock.Verify(f => f.CreatePrinterStream("COM0", 125_000));
            serialPortStreamMock.Verify(s => s.DiscardInBuffer());
            serialPortStreamMock.Verify(s => s.DiscardOutBuffer());
            serialPortStreamMock.Verify(s => s.Open());

            Assert.Equal(PrinterState.Ready, printer.State);
        }

        [Fact]
        public async Task ConnectAsync_CallWhenAlreadyConnected_ThrowsException()
        {
            MarlinPrinter printer = await CreateConnectedPrinter();

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await printer.ConnectAsync(TestHelpers.CreateTimeOutToken());
            });
        }

        [Fact]
        public async Task Dispose_WithConnectedPrinter_ClosesConnection()
        {
            MarlinPrinter printer = await CreateConnectedPrinter();
            printer.Dispose();
            Assert.Equal(PrinterState.Disconnected, printer.State);
        }

        [Fact]
        public async Task Dispose_WhenAlreadyDisposed_DoesNotBreak()
        {
            MarlinPrinter printer = await CreateConnectedPrinter();
            printer.Dispose();
            printer.Dispose();
            Assert.Equal(PrinterState.Disconnected, printer.State);
        }

        [Fact]
        public async Task ConnectedPrinter_ReceivesTemperaturesMessage_ParsesTemperatures()
        {
            SerialPrinterStreamSimulator sim = new();
            Mock<ISerialPort> serialPortStreamMock = CreateSerialPort("COM0", 125_000, sim);

            MarlinPrinter printer = await CreateConnectedPrinter(sim);

            sim.SendMessage("message reporting something");
            sim.SendMessage("T0:136.73 /210.00 B:23.98 /60.00 T0:136.73 /210.00 T1:162.94 /0.00 @:127 B@:127 @0:127 @1:0");

            await Task.Delay(100);

            Assert.NotNull(printer.Temperatures);

            Assert.Collection(
                printer.Temperatures!.HotendTemperatures,
                t0 =>
                {
                    Assert.Equal(136.73, t0.Current);
                    Assert.Equal(210.00, t0.Target);
                },
                t1 =>
                {
                    Assert.Equal(162.94, t1.Current);
                    Assert.Equal(0.00, t1.Target);
                });

            Assert.Equal(136.73, printer.Temperatures.ActiveHotendTemperature.Current);
            Assert.Equal(210.00, printer.Temperatures.ActiveHotendTemperature.Target);

            Assert.NotNull(printer.Temperatures.BedTemperature);
            Assert.Equal(23.98, printer.Temperatures.BedTemperature!.Current);
            Assert.Equal(60.00, printer.Temperatures.BedTemperature.Target);
        }

        [Fact]
        public async Task ConnectedPrinter_ReceivesTemperaturesMessage_HandlesUnexpectedSensorNames()
        {
            SerialPrinterStreamSimulator sim = new();

            MarlinPrinter printer = await CreateConnectedPrinter(sim);

            sim.SendMessage("T0:136.73 /210.00 C:23.98 /60.00 T0:136.73 /210.00 @:127 C@:127 @0:127");

            await Task.Delay(5);

            Assert.NotNull(printer.Temperatures);
            Assert.Single(printer.Temperatures!.HotendTemperatures);
        }

        [Fact]
        public async Task SendCommandAsync_WhenPrinterDisposed_ThrowsException()
        {
            MarlinPrinter printer = await CreateConnectedPrinter();

            printer.Dispose();

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await printer.SendCommandAsync("blah", TestHelpers.CreateTimeOutToken());
            });

            Assert.Equal("Printer isn't connected", exception.Message);
        }

        [Fact]
        public async Task SendCommandBlockAsync_WithMultipleCommands_SendsAllCommands()
        {
            SerialPrinterStreamSimulator sim = new();
            Mock<ISerialPort> serialPortStreamMock = CreateSerialPort("COM0", 125_000, sim);

            MarlinPrinter printer = await CreateConnectedPrinter(sim);

            sim.RegisterResponse(new Regex(".*"), "ok");

            await printer.SendCommandBlockAsync(
                "G0 X0 Y0 Z10 ; move to 0,0\n" +
                "M104 S210\n" +
                "M140 S60\n",
                TestHelpers.CreateTimeOutToken(2000));

            await Task.Delay(5);

            Assert.Collection(
                sim.GetWrittenLines(),
                l => Assert.Equal("N0 M110 N0*125", l),
                l => Assert.Equal("N1 M155 S1*97", l),
                l => Assert.Equal("N2 G0 X0 Y0 Z10*81", l),
                l => Assert.Equal("N3 M104 S210*101", l),
                l => Assert.Equal("N4 M140 S60*87", l));
        }

        [Fact]
        public async Task DisconnectAsync_WhenPrinterIsIdle_DisconnectsSuccessfully()
        {
            MarlinPrinter printer = await CreateConnectedPrinter();

            Task disconnectTask = printer.DisconnectAsync();

            Assert.Equal(PrinterState.Disconnecting, printer.State);

            await disconnectTask;

            Assert.Equal(PrinterState.Disconnected, printer.State);
        }

        [Fact]
        public async Task DisconnectAsync_WhenPrinterIsPrinting_DisconnectsSuccessfully()
        {
            MarlinPrinter printer = await CreateConnectedPrinter();

            using MemoryStream printStream = new(Encoding.ASCII.GetBytes("G0 X0 Y0"));

            await printer.StartPrintAsync(printStream, CancellationToken.None);

            Assert.Equal(PrinterState.Printing, printer.State);

            await printer.DisconnectAsync();

            Assert.Equal(PrinterState.Disconnected, printer.State);
        }

        [Fact]
        public async Task StartPrintAsync_WhenPrinterIsNotReady_ThrowsException()
        {
            Mock<ISerialPortFactory> serialPortStreamFactoryMock = new();
            serialPortStreamFactoryMock.Setup(f => f.CreatePrinterStream(It.IsAny<string>(), It.IsAny<int>())).Returns<string, int>((s, i) => CreateSerialPort("COM0", 125_000, new MemoryStream()).Object);

            MarlinPrinter printer = new(serialPortStreamFactoryMock.Object, TestHelpers.CreateLogger<MarlinPrinter>().Object, "COM0", 125_000);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await printer.StartPrintAsync(new MemoryStream(), CancellationToken.None);
            });

            Assert.Equal("Printer isn't ready", exception.Message);
        }

        private static async Task<MarlinPrinter> CreateConnectedPrinter(SerialPrinterStreamSimulator? sim = null, bool canReportTemperatures = true)
        {
            if (sim == null)
            {
                sim = new();
            }

            Mock<ISerialPort> mock = CreateSerialPort("COM0", 125_000, sim);

            sim.RegisterResponse("N0 M110 N0*125", "ok");
            sim.RegisterResponse(new Regex(@"N1 (M155 S\d+)\*\d+"), canReportTemperatures ? "ok" : "echo:Unknown command: \"$1\"\nok");

            Mock<ISerialPortFactory> serialPortStreamFactoryMock = new();
            serialPortStreamFactoryMock.Setup(f => f.CreatePrinterStream(It.IsAny<string>(), It.IsAny<int>())).Returns<string, int>((s, i) => mock.Object);

            MarlinPrinter printer = new(serialPortStreamFactoryMock.Object, TestHelpers.CreateLogger<MarlinPrinter>().Object, "COM0", 125_000);

            Task connectTask = printer.ConnectAsync(TestHelpers.CreateTimeOutToken());
            sim.SendMessage("start");
            await connectTask;

            return printer;
        }

        private static Mock<ISerialPort> CreateSerialPort(string portName, int baudRate, Stream baseStream)
        {
            Mock<ISerialPort> serialPortMock = new();
            serialPortMock.SetupGet(p => p.PortName).Returns(portName);
            serialPortMock.SetupGet(p => p.BaudRate).Returns(baudRate);
            serialPortMock.SetupGet(p => p.Encoding).Returns(Encoding.ASCII);
            serialPortMock.SetupGet(p => p.NewLine).Returns("\n");
            serialPortMock.SetupGet(p => p.DtrEnable).Returns(true);
            serialPortMock.SetupGet(p => p.RtsEnable).Returns(true);
            serialPortMock.SetupGet(p => p.BaseStream).Returns(baseStream);

            return serialPortMock;
        }
    }
}
