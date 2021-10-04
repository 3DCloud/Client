using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Print3DCloud.Client.Printers.Marlin;
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

            sim.RespondTo("M110 N0", "ok");

            SerialCommandManager serialCommandManager = CreateSerialCommandManager(sim);
            Task t = serialCommandManager.WaitForStartupAsync(new CancellationTokenSource(500).Token);

            sim.SendMessage("start");

            await t;

            Assert.Equal(new[] { "M110 N0" }, sim.GetWrittenLines());
        }

        [Fact]
        public async Task WaitForStartupAsync_WhenNoStartIsReceived_TimesOut()
        {
            using SerialPrinterStreamSimulator sim = new();

            sim.RespondTo("M110 N0", "ok");

            SerialCommandManager serialCommandManager = CreateSerialCommandManager(sim);
            Task t = serialCommandManager.WaitForStartupAsync(new CancellationTokenSource(500).Token);

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
            Task t = serialCommandManager.WaitForStartupAsync(new CancellationTokenSource(500).Token);

            sim.SendMessage("start");

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await t;
            });

            Assert.Equal(new[] { "M110 N0" }, sim.GetWrittenLines());
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
    }
}
