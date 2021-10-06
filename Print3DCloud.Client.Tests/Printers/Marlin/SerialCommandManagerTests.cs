using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Print3DCloud.Client.Printers;
using Print3DCloud.Client.Printers.Marlin;
using Print3DCloud.Client.Tests.TestUtilities;
using Xunit;

namespace Print3DCloud.Client.Tests.Printers.Marlin
{
    public class SerialCommandManagerTests
    {
        [Fact]
        public void Constructor_WithValidArguments_CreatesSerialCommandManager()
        {
            SerialCommandManager serialCommandManager = CreateSerialCommandManager();

            Assert.NotNull(serialCommandManager);
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsException()
        {
            Mock<ILogger<MarlinPrinter>> loggerMock = new();
            MemoryStream stream = new();

            Assert.Throws<ArgumentNullException>("logger", () =>
            {
                _ = new SerialCommandManager(null!, stream, Encoding.ASCII, "\n");
            });
        }

        [Fact]
        public void Constructor_WithNullStream_ThrowsException()
        {
            Mock<ILogger<MarlinPrinter>> loggerMock = new();
            MemoryStream stream = new();

            Assert.Throws<ArgumentNullException>("stream", () =>
            {
                _ = new SerialCommandManager(loggerMock.Object, null!, Encoding.ASCII, "\n");
            });
        }

        [Fact]
        public void Constructor_WithNullEncoding_ThrowsException()
        {
            Mock<ILogger<MarlinPrinter>> loggerMock = new();
            MemoryStream stream = new();

            Assert.Throws<ArgumentNullException>("encoding", () =>
            {
                _ = new SerialCommandManager(loggerMock.Object, stream, null!, "\n");
            });
        }

        [Fact]
        public void Constructor_WithNullNewLine_ThrowsException()
        {
            Mock<ILogger<MarlinPrinter>> loggerMock = new();
            MemoryStream stream = new();

            Assert.Throws<ArgumentNullException>("newLine", () =>
            {
                _ = new SerialCommandManager(loggerMock.Object, stream, Encoding.ASCII, null!);
            });
        }

        [Fact]
        public void Constructor_WithEmptyNewLine_ThrowsException()
        {
            Mock<ILogger<MarlinPrinter>> loggerMock = new();
            MemoryStream stream = new();

            Assert.Throws<ArgumentNullException>("newLine", () =>
            {
                _ = new SerialCommandManager(loggerMock.Object, stream, Encoding.ASCII, string.Empty);
            });
        }

        [Fact]
        public async Task WaitForStartupAsync_WhenValidCommandsReceived_CompletesSuccessfully()
        {
            using SerialPrinterStreamSimulator sim = new();

            sim.RegisterResponse("N0 M110 N0*125", "ok");

            SerialCommandManager serialCommandManager = CreateSerialCommandManager(sim);
            Task t = serialCommandManager.WaitForStartupAsync(TestHelpers.CreateTimeOutToken());

            sim.SendMessage("start");

            await t;

            Assert.Equal(new[] { "N0 M110 N0*125" }, sim.GetWrittenLines());
        }

        [Fact]
        public async Task WaitForStartupAsync_WhenNoStartIsReceived_TimesOut()
        {
            using SerialPrinterStreamSimulator sim = new();

            sim.RegisterResponse("N0 M110 N0*125", "ok");

            SerialCommandManager serialCommandManager = CreateSerialCommandManager(sim);
            Task t = serialCommandManager.WaitForStartupAsync(TestHelpers.CreateTimeOutToken());

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await t;
            });

            Assert.Equal(Array.Empty<string>(), sim.GetWrittenLines());
        }

        [Fact]
        public async Task WaitForStartupAsync_WhenNoResponseToSetLineCommandIsReceived_TimesOut()
        {
            using SerialPrinterStreamSimulator sim = new();

            SerialCommandManager serialCommandManager = CreateSerialCommandManager(sim);
            Task t = serialCommandManager.WaitForStartupAsync(TestHelpers.CreateTimeOutToken());

            sim.SendMessage("start");

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await t;
            });

            Assert.Equal(new[] { "N0 M110 N0*125" }, sim.GetWrittenLines());
        }

        [Fact]
        public async Task SendCommandAsync_WithValidCommand_SendsCommand()
        {
            using SerialPrinterStreamSimulator sim = new();

            sim.RegisterResponse(new Regex(@"N\d+ M104 S210\*\d+"), "ok");

            SerialCommandManager serialCommandManager = await SetUpConnectedPrinter(sim);

            Task commandTask = serialCommandManager.SendCommandAsync("M104 S210", TestHelpers.CreateTimeOutToken());
            await serialCommandManager.ReceiveLineAsync(TestHelpers.CreateTimeOutToken());
            await commandTask;

            Assert.Equal(new[] { "N0 M110 N0*125", "N1 M104 S210*103" }, sim.GetWrittenLines());
        }

        [Fact]
        public async Task SendCommandAsync_WithCommandContainingNewLines_SendsOnlyFirstLine()
        {
            using SerialPrinterStreamSimulator sim = new();

            sim.RegisterResponse(new Regex(@"N\d+ M104 S210\*\d+"), "ok");

            SerialCommandManager serialCommandManager = await SetUpConnectedPrinter(sim);

            Task commandTask = serialCommandManager.SendCommandAsync("M104 S210\nm140 S60", TestHelpers.CreateTimeOutToken());
            await serialCommandManager.ReceiveLineAsync(TestHelpers.CreateTimeOutToken());
            await commandTask;

            Assert.Equal(new[] { "N0 M110 N0*125", "N1 M104 S210*103" }, sim.GetWrittenLines());
        }

        [Fact]
        public async Task SendCommandAsync_WithValidCommands_IncrementsLineNumber()
        {
            using SerialPrinterStreamSimulator sim = new();

            sim.RegisterResponse(new Regex(@"N\d+ M104 S210\*\d+"), "ok");
            sim.RegisterResponse(new Regex(@"N\d+ M140 S60\*\d+"), "ok");

            SerialCommandManager serialCommandManager = await SetUpConnectedPrinter(sim);

            Task commandTask = serialCommandManager.SendCommandAsync("M104 S210", TestHelpers.CreateTimeOutToken());
            await serialCommandManager.ReceiveLineAsync(TestHelpers.CreateTimeOutToken());
            await commandTask;

            commandTask = serialCommandManager.SendCommandAsync("M140 S60", TestHelpers.CreateTimeOutToken());
            await serialCommandManager.ReceiveLineAsync(TestHelpers.CreateTimeOutToken());
            await commandTask;

            Assert.Equal(new[] { "N0 M110 N0*125", "N1 M104 S210*103", "N2 M140 S60*81" }, sim.GetWrittenLines());
        }

        [Fact]
        public async Task SendCommandAsync_WhenLineNumberLimitReached_ResetsLineNumber()
        {
            using SerialPrinterStreamSimulator sim = new();

            sim.RegisterResponse(new Regex(@"N\d+ M104 S210\*\d+"), "ok");
            sim.RegisterResponse(new Regex(@"N\d+ M140 S60\*\d+"), "ok");

            SerialCommandManager serialCommandManager = await SetUpConnectedPrinter(sim);

            // this is not good practice, but sending int.MaxValue messages isn't great either
            typeof(SerialCommandManager).GetField("currentLineNumber", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(serialCommandManager, int.MaxValue - 1);

            Task commandTask = serialCommandManager.SendCommandAsync("M104 S210", TestHelpers.CreateTimeOutToken());
            await serialCommandManager.ReceiveLineAsync(TestHelpers.CreateTimeOutToken());
            await commandTask;

            commandTask = serialCommandManager.SendCommandAsync("M140 S60", TestHelpers.CreateTimeOutToken());
            await serialCommandManager.ReceiveLineAsync(TestHelpers.CreateTimeOutToken());
            await serialCommandManager.ReceiveLineAsync(TestHelpers.CreateTimeOutToken());
            await commandTask;

            Assert.Equal(new[] { "N0 M110 N0*125", "N2147483646 M104 S210*93", "N0 M110 N0*125", "N1 M140 S60*82" }, sim.GetWrittenLines());
        }

        [Fact]
        public async Task SendCommandAsync_WhenResendRequested_ResendsLine()
        {
            using SerialPrinterStreamSimulator sim = new();

            sim.RegisterResponse(new Regex(@"N(\d+) M104 S210\*\d+"), "Error:checksum mismatch, Last Line: 0\nResend: $1\nok", 1);
            sim.RegisterResponse(new Regex(@"N\d+ M104 S210\*\d+"), "ok", 1);

            SerialCommandManager serialCommandManager = await SetUpConnectedPrinter(sim);

            Task commandTask = serialCommandManager.SendCommandAsync("M104 S210", TestHelpers.CreateTimeOutToken());

            // first response
            MarlinMessage message = await serialCommandManager.ReceiveLineAsync(TestHelpers.CreateTimeOutToken());
            Assert.Equal("Error:checksum mismatch, Last Line: 0", message.Content);
            Assert.Equal(MarlinMessageType.Error, message.Type);

            message = await serialCommandManager.ReceiveLineAsync(TestHelpers.CreateTimeOutToken());
            Assert.Equal("Resend: 1", message.Content);
            Assert.Equal(MarlinMessageType.ResendLine, message.Type);

            message = await serialCommandManager.ReceiveLineAsync(TestHelpers.CreateTimeOutToken());
            Assert.Equal("ok", message.Content);
            Assert.Equal(MarlinMessageType.CommandAcknowledgement, message.Type);

            // second response
            message = await serialCommandManager.ReceiveLineAsync(TestHelpers.CreateTimeOutToken());
            Assert.Equal("ok", message.Content);
            Assert.Equal(MarlinMessageType.CommandAcknowledgement, message.Type);

            await commandTask;
        }

        [Fact]
        public async Task SendCommandAsync_WhenResendRequestedWithUnexpectedLineNumber_ThrowsException()
        {
            using SerialPrinterStreamSimulator sim = new();

            sim.RegisterResponse(new Regex(@"N(\d+) M104 S210\*\d+"), "Error:checksum mismatch, Last Line: 0\nResend: 5\nok", 1);
            sim.RegisterResponse(new Regex(@"N\d+ M104 S210\*\d+"), "ok", 1);

            SerialCommandManager serialCommandManager = await SetUpConnectedPrinter(sim);

            Task commandTask = serialCommandManager.SendCommandAsync("M104 S210", TestHelpers.CreateTimeOutToken());

            MarlinMessage message = await serialCommandManager.ReceiveLineAsync(TestHelpers.CreateTimeOutToken());
            Assert.Equal("Error:checksum mismatch, Last Line: 0", message.Content);
            Assert.Equal(MarlinMessageType.Error, message.Type);

            message = await serialCommandManager.ReceiveLineAsync(TestHelpers.CreateTimeOutToken());
            Assert.Equal("Resend: 5", message.Content);
            Assert.Equal(MarlinMessageType.ResendLine, message.Type);

            message = await serialCommandManager.ReceiveLineAsync(TestHelpers.CreateTimeOutToken());
            Assert.Equal("ok", message.Content);
            Assert.Equal(MarlinMessageType.CommandAcknowledgement, message.Type);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await commandTask;
            });
        }

        [Fact]
        public async Task SendCommandAsync_WhenResendRequestedTooManyTimes_ThrowsException()
        {
            using SerialPrinterStreamSimulator sim = new();

            sim.RegisterResponse(new Regex(@"N(\d+) M104 S210\*\d+"), "Error:checksum mismatch, Last Line: 0\nResend: $1\nok");

            SerialCommandManager serialCommandManager = await SetUpConnectedPrinter(sim);

            Task commandTask = serialCommandManager.SendCommandAsync("M104 S210", TestHelpers.CreateTimeOutToken());

            for (int i = 0; i < 5; i++)
            {
                MarlinMessage message = await serialCommandManager.ReceiveLineAsync(TestHelpers.CreateTimeOutToken());
                Assert.Equal("Error:checksum mismatch, Last Line: 0", message.Content);
                Assert.Equal(MarlinMessageType.Error, message.Type);

                message = await serialCommandManager.ReceiveLineAsync(TestHelpers.CreateTimeOutToken());
                Assert.Equal("Resend: 1", message.Content);
                Assert.Equal(MarlinMessageType.ResendLine, message.Type);

                message = await serialCommandManager.ReceiveLineAsync(TestHelpers.CreateTimeOutToken());
                Assert.Equal("ok", message.Content);
                Assert.Equal(MarlinMessageType.CommandAcknowledgement, message.Type);
            }

            IOException exception = await Assert.ThrowsAsync<IOException>(async () =>
            {
                await commandTask;
            });

            Assert.Equal("Printer requested resend too many times", exception.Message);
        }

        [Fact]
        public async Task SendCommandAsync_WhenNotConnected_ThrowsException()
        {
            using SerialPrinterStreamSimulator sim = new();

            sim.RegisterResponse("N0 M110 N0*125", "ok");

            SerialCommandManager serialCommandManager = CreateSerialCommandManager(sim);

            Exception exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await serialCommandManager.SendCommandAsync("blah", TestHelpers.CreateTimeOutToken());
            });

            Assert.Equal("Startup did not complete", exception.Message);
            Assert.Equal(Array.Empty<string>(), sim.GetWrittenLines());
        }

        [Fact]
        public async Task SendCommandAsync_WhenCommandIsEmpty_DoesNothing()
        {
            using SerialPrinterStreamSimulator sim = new();
            SerialCommandManager serialCommandManager = await SetUpConnectedPrinter(sim);

            await serialCommandManager.SendCommandAsync(" ", TestHelpers.CreateTimeOutToken());

            Assert.Equal(new[] { "N0 M110 N0*125" }, sim.GetWrittenLines());
        }

        [Fact]
        public async Task SendCommandAsync_WhenCommandIsFullLineComment_DoesNothing()
        {
            using SerialPrinterStreamSimulator sim = new();
            SerialCommandManager serialCommandManager = await SetUpConnectedPrinter(sim);

            await serialCommandManager.SendCommandAsync(" ; this is a comment with no command beforehand", TestHelpers.CreateTimeOutToken());

            Assert.Equal(new[] { "N0 M110 N0*125" }, sim.GetWrittenLines());
        }

        [Fact]
        public async Task ReceiveLineAsync_WhenNotConnected_ThrowsException()
        {
            using SerialPrinterStreamSimulator sim = new();
            SerialCommandManager serialCommandManager = CreateSerialCommandManager(sim);

            sim.SendMessage("start");

            Exception exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await serialCommandManager.ReceiveLineAsync(TestHelpers.CreateTimeOutToken());
            });

            Assert.Equal("Startup did not complete", exception.Message);
        }

        [Fact]
        public async Task ReceiveLineAsync_WhenUnknownCommandReceived_IdentifiesUnknownCommand()
        {
            using SerialPrinterStreamSimulator sim = new();
            SerialCommandManager serialCommandManager = await SetUpConnectedPrinter(sim);

            sim.SendMessage("echo:Unknown command: \"M12345 S3568901\"\nok");

            Task commandTask = serialCommandManager.SendCommandAsync("M12345 S3568901", TestHelpers.CreateTimeOutToken());
            MarlinMessage message = await serialCommandManager.ReceiveLineAsync(TestHelpers.CreateTimeOutToken());
            await serialCommandManager.ReceiveLineAsync(TestHelpers.CreateTimeOutToken());
            await commandTask;

            Assert.Equal("echo:Unknown command: \"M12345 S3568901\"", message.Content);
            Assert.Equal(MarlinMessageType.UnknownCommand, message.Type);
        }

        [Fact]
        public async Task ReceiveLineAsync_WhenStandardMessageReceived_ReturnsAsSimpleMessage()
        {
            using SerialPrinterStreamSimulator sim = new();
            SerialCommandManager serialCommandManager = await SetUpConnectedPrinter(sim);

            string content = "T:210.00 /210.00 B:26.19 /0.00 T0:210.00 /210.00 T1:64.45 /0.00 @:39 B@:0 @0:39 @1:0";
            sim.SendMessage(content);

            MarlinMessage message = await serialCommandManager.ReceiveLineAsync(TestHelpers.CreateTimeOutToken());

            Assert.Equal(content, message.Content);
            Assert.Equal(MarlinMessageType.Message, message.Type);
        }

        [Fact]
        public async Task ReceiveLineAsync_WhenFatalErrorReceived_ThrowsException()
        {
            using SerialPrinterStreamSimulator sim = new();
            SerialCommandManager serialCommandManager = await SetUpConnectedPrinter(sim);

            sim.SendMessage("Error:Printer halted. kill() called!");

            await Assert.ThrowsAsync<PrinterHaltedException>(async () =>
            {
                await serialCommandManager.ReceiveLineAsync(TestHelpers.CreateTimeOutToken());
            });
        }

        [Fact]
        public async Task ReceiveLineAsync_WhenPrinterRestarts_ThrowsException()
        {
            using SerialPrinterStreamSimulator sim = new();
            SerialCommandManager serialCommandManager = await SetUpConnectedPrinter(sim);

            sim.SendMessage("start");

            await Assert.ThrowsAsync<PrinterHaltedException>(async () =>
            {
                await serialCommandManager.ReceiveLineAsync(TestHelpers.CreateTimeOutToken());
            });
        }

        private static SerialCommandManager CreateSerialCommandManager(Stream? stream = null, ILogger<MarlinPrinter>? logger = null)
        {
            if (stream == null)
            {
                Mock<Stream> streamMock = new();
                streamMock.SetupGet(s => s.CanRead).Returns(true);
                streamMock.SetupGet(s => s.CanWrite).Returns(true);
                stream = streamMock.Object;
            }

            if (logger == null)
            {
                logger = new Mock<ILogger<MarlinPrinter>>().Object;
            }

            return new SerialCommandManager(logger, stream, Encoding.ASCII, "\n");
        }

        private static async Task<SerialCommandManager> SetUpConnectedPrinter(SerialPrinterStreamSimulator sim)
        {
            SerialCommandManager serialCommandManager = CreateSerialCommandManager(sim);

            sim.SendMessage("start");
            sim.RegisterResponse("N0 M110 N0*125", "ok");

            await serialCommandManager.WaitForStartupAsync(TestHelpers.CreateTimeOutToken());

            return serialCommandManager;
        }
    }
}
