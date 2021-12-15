using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ActionCableSharp;
using Moq;
using Print3DCloud.Client.Printers;
using Print3DCloud.Client.Printers.Marlin;
using Print3DCloud.Client.Tests.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Print3DCloud.Client.Tests.Printers.Marlin
{
    public class MarlinPrinterTests
    {
        private readonly ITestOutputHelper testOutputHelper;

        public MarlinPrinterTests(ITestOutputHelper testOutputHelper)
        {
            this.testOutputHelper = testOutputHelper;
        }

        [Fact]
        public async Task ConnectAsync_WhenPrinterRespondsAsExpected_ConnectsSuccessfully()
        {
            SerialPrinterStreamSimulator sim = new();
            Mock<ISerialPort> serialPortStreamMock = CreateSerialPort("COM0", 125_000, sim);

            sim.RegisterResponse("N0 M110 N0*125", "ok");
            sim.RegisterResponse(
                new Regex(@"N\d+ M115\*\d+"),
                "FIRMWARE_NAME:Marlin 1.1.0 (Github) SOURCE_CODE_URL:https://github.com/MarlinFirmware/Marlin PROTOCOL_VERSION:1.0 MACHINE_TYPE:RepRap EXTRUDER_COUNT:1 UUID:cede2a2f-41a2-4748-9b12-c55c62f367ff\nCap:AUTOREPORT_TEMP:1\nok\n");
            sim.RegisterResponse(new Regex(@"N\d+ M155 S\d+\*\d+"), "ok");
            sim.RegisterResponse(new Regex(@"N\d+ M503\*\d+"), "ok");

            Mock<ISerialPortFactory> serialPortStreamFactoryMock = new();
            serialPortStreamFactoryMock.Setup(f => f.CreateSerialPort(It.IsAny<string>(), It.IsAny<int>())).Returns(() => serialPortStreamMock.Object);

            MarlinPrinter printer = new(
                TestHelpers.CreateLogger<MarlinPrinter>(this.testOutputHelper),
                new Mock<IActionCableSubscription>().Object,
                serialPortStreamFactoryMock.Object,
                "COM0",
                125_000);

            Assert.Equal(PrinterState.Disconnected, printer.State);

            Task connectTask = printer.ConnectAsync(TestHelpers.CreateTimeOutToken());

            Assert.Equal(PrinterState.Connecting, printer.State);

            sim.SendMessage("start");

            await connectTask;

            serialPortStreamFactoryMock.Verify(f => f.CreateSerialPort("COM0", 125_000));
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
            sim.RegisterResponse(
                new Regex(@"N\d+ M115\*\d+"),
                "FIRMWARE_NAME:Marlin 1.1.0 (Github) SOURCE_CODE_URL:https://github.com/MarlinFirmware/Marlin PROTOCOL_VERSION:1.0 MACHINE_TYPE:RepRap EXTRUDER_COUNT:1 UUID:cede2a2f-41a2-4748-9b12-c55c62f367ff\nok\n");
            sim.RegisterResponse(new Regex(@"N\d+ M155 S\d+\*\d+"), "ok");
            sim.RegisterResponse(new Regex(@"N\d+ M503\*\d+"), "ok");

            Mock<ISerialPortFactory> serialPortStreamFactoryMock = new();
            serialPortStreamFactoryMock.Setup(f => f.CreateSerialPort(It.IsAny<string>(), It.IsAny<int>())).Returns(() => serialPortStreamMock.Object);

            MarlinPrinter printer = new(
                TestHelpers.CreateLogger<MarlinPrinter>(this.testOutputHelper),
                new Mock<IActionCableSubscription>().Object,
                serialPortStreamFactoryMock.Object,
                "COM0",
                125_000);

            Assert.Equal(PrinterState.Disconnected, printer.State);

            Task connectTask = printer.ConnectAsync(TestHelpers.CreateTimeOutToken());

            Assert.Equal(PrinterState.Connecting, printer.State);

            sim.SendMessage("start");

            await connectTask;

            serialPortStreamFactoryMock.Verify(f => f.CreateSerialPort("COM0", 125_000));
            serialPortStreamMock.Verify(s => s.DiscardInBuffer());
            serialPortStreamMock.Verify(s => s.DiscardOutBuffer());
            serialPortStreamMock.Verify(s => s.Open());

            Assert.Equal(PrinterState.Ready, printer.State);
        }

        [Fact]
        public async Task ConnectAsync_CallWhenAlreadyConnected_ThrowsException()
        {
            MarlinPrinter printer = await this.CreateConnectedPrinter();

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await printer.ConnectAsync(TestHelpers.CreateTimeOutToken());
            });

            Assert.Equal("Printer is already connected", exception.Message);
        }

        [Fact]
        public async Task Dispose_WithConnectedPrinter_ClosesConnection()
        {
            MarlinPrinter printer = await this.CreateConnectedPrinter();
            printer.Dispose();
            Assert.Equal(PrinterState.Disconnected, printer.State);
        }

        [Fact]
        public async Task Dispose_WhenAlreadyDisposed_DoesNotBreak()
        {
            MarlinPrinter printer = await this.CreateConnectedPrinter();
            printer.Dispose();
            printer.Dispose();
            Assert.Equal(PrinterState.Disconnected, printer.State);
        }

        [Fact]
        public async Task ConnectedPrinter_ReceivesTemperaturesMessage_ParsesTemperatures()
        {
            SerialPrinterStreamSimulator sim = new();

            MarlinPrinter printer = await this.CreateConnectedPrinter(sim);

            sim.SendMessage("message reporting something");
            sim.SendMessage("T0:136.73 /210.00 B:23.98 /60.00 T0:136.73 /210.00 T1:162.94 /0.00 @:127 B@:127 @0:127 @1:0");

            await Task.Delay(100);

            Assert.NotNull(printer.Temperatures);

            Assert.Collection(
                printer.Temperatures!.HotendTemperatures,
                t =>
                {
                    Assert.Equal(136.73, t.Current);
                    Assert.Equal(210.00, t.Target);
                },
                t =>
                {
                    Assert.Equal(162.94, t.Current);
                    Assert.Equal(0.00, t.Target);
                });

            Assert.NotNull(printer.Temperatures.BedTemperature);
            Assert.Equal(23.98, printer.Temperatures.BedTemperature!.Current);
            Assert.Equal(60.00, printer.Temperatures.BedTemperature.Target);
        }

        [Fact]
        public async Task ConnectedPrinter_ReceivesTemperaturesMessage_HandlesUnexpectedSensorNames()
        {
            SerialPrinterStreamSimulator sim = new();

            MarlinPrinter printer = await this.CreateConnectedPrinter(sim);

            sim.SendMessage("T0:136.73 /210.00 C:23.98 /60.00 T0:136.73 /210.00 @:127 C@:127 @0:127");

            await Task.Delay(20);

            Assert.NotNull(printer.Temperatures);
            Assert.Single(printer.Temperatures!.HotendTemperatures);
        }

        [Fact]
        public async Task SendCommandAsync_WhenPrinterDisposed_ThrowsException()
        {
            MarlinPrinter printer = await this.CreateConnectedPrinter();

            printer.Dispose();

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await printer.SendCommandAsync("blah", TestHelpers.CreateTimeOutToken());
            });

            Assert.Equal("Printer isn't connected", exception.Message);
        }

        [Fact]
        public async Task DisconnectAsync_WhenPrinterIsIdle_DisconnectsSuccessfully()
        {
            MarlinPrinter printer = await this.CreateConnectedPrinter();

            Task disconnectTask = printer.DisconnectAsync(TestHelpers.CreateTimeOutToken());

            Assert.Equal(PrinterState.Disconnecting, printer.State);

            await disconnectTask;

            Assert.Equal(PrinterState.Disconnected, printer.State);
        }

        [Fact]
        public async Task DisconnectAsync_WhenPrinterIsPrinting_DisconnectsSuccessfully()
        {
            SerialPrinterStreamSimulator sim = new();
            MarlinPrinter printer = await this.CreateConnectedPrinter(sim);

            await using MemoryStream printStream = new(Encoding.ASCII.GetBytes("G0 X0 Y0"));

            _ = printer.StartPrintAsync(printStream, TestHelpers.CreateTimeOutToken());

            Assert.Equal(PrinterState.Downloading, printer.State);

            sim.SendMessage("ok");

            Task disconnectTask = printer.DisconnectAsync(TestHelpers.CreateTimeOutToken());

            Assert.Equal(PrinterState.Disconnecting, printer.State);

            await disconnectTask;

            Assert.Equal(PrinterState.Disconnected, printer.State);
        }

        private static Mock<ISerialPort> CreateSerialPort(string portName, int baudRate, Stream baseStream)
        {
            Mock<ISerialPort> serialPortMock = new();
            serialPortMock.SetupGet(p => p.PortName).Returns(portName);
            serialPortMock.SetupGet(p => p.BaudRate).Returns(baudRate);
            serialPortMock.SetupGet(p => p.BaseStream).Returns(baseStream);

            return serialPortMock;
        }

        private async Task<MarlinPrinter> CreateConnectedPrinter(SerialPrinterStreamSimulator? sim = null, bool canReportTemperatures = true)
        {
            sim ??= new SerialPrinterStreamSimulator();

            Mock<ISerialPort> mock = CreateSerialPort("COM0", 125_000, sim);

            string firmwareInfo =
                "FIRMWARE_NAME:Marlin 1.1.0 (Github) SOURCE_CODE_URL:https://github.com/MarlinFirmware/Marlin PROTOCOL_VERSION:1.0 MACHINE_TYPE:RepRap EXTRUDER_COUNT:1 UUID:cede2a2f-41a2-4748-9b12-c55c62f367ff\n";

            if (canReportTemperatures)
            {
                firmwareInfo += "Cap:AUTOREPORT_TEMP:1\n";
            }

            firmwareInfo += "ok\n";

            sim.RegisterResponse("N0 M110 N0*125", "ok");
            sim.RegisterResponse(new Regex(@"N\d+ M115\*\d+"), firmwareInfo);
            sim.RegisterResponse(new Regex(@"N\d+ M155 S\d+\*\d+"), "ok");
            sim.RegisterResponse(new Regex(@"N\d+ M503\*\d+"), "ok");

            Mock<ISerialPortFactory> serialPortStreamFactoryMock = new();
            serialPortStreamFactoryMock.Setup(f => f.CreateSerialPort(It.IsAny<string>(), It.IsAny<int>())).Returns(() => mock.Object);

            MarlinPrinter printer = new(
                TestHelpers.CreateLogger<MarlinPrinter>(this.testOutputHelper),
                new Mock<IActionCableSubscription>().Object,
                serialPortStreamFactoryMock.Object,
                "COM0",
                125_000);

            Task connectTask = printer.ConnectAsync(TestHelpers.CreateTimeOutToken());
            sim.SendMessage("start");
            await connectTask;

            return printer;
        }
    }
}
