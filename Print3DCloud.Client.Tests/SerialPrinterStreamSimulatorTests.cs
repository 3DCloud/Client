using System.IO;
using System.Text;
using Xunit;

namespace Print3DCloud.Client.Tests
{
    public class SerialPrinterStreamSimulatorTests
    {
        [Fact]
        public void SendMessage_WithValidMessage_WritesToInputStream()
        {
            using SerialPrinterStreamSimulator sim = new();

            sim.SendMessage("start");

            using StreamReader reader = GetStreamReader(sim);

            Assert.Equal(0, sim.Position);
            Assert.False(reader.EndOfStream);
            Assert.Equal("start", reader.ReadLine());
        }

        [Fact]
        public void RespondTo_WithMessageSentAlone_Responds()
        {
            using SerialPrinterStreamSimulator sim = new();
            using StreamWriter writer = GetStreamWriter(sim);

            sim.RespondTo("M155", "ok");
            writer.WriteLine("M155");

            using StreamReader reader = GetStreamReader(sim);

            Assert.False(reader.EndOfStream);
            Assert.Equal("ok", reader.ReadLine());
        }

        [Fact]
        public void RespondTo_WithMessageSentInParts_Responds()
        {
            using SerialPrinterStreamSimulator sim = new();
            using StreamWriter writer = GetStreamWriter(sim);

            sim.RespondTo("M155", "ok");

            writer.Write("M1");
            writer.Write("55\n");

            using StreamReader reader = GetStreamReader(sim);

            Assert.False(reader.EndOfStream);
            Assert.Equal("ok", reader.ReadLine());
        }

        [Fact]
        public void RespondTo_WithMessageSentWithOtherMessages_Responds()
        {
            using SerialPrinterStreamSimulator sim = new();
            using StreamWriter writer = GetStreamWriter(sim);

            sim.RespondTo("M155", "ok");
            writer.WriteLine("G0 X0 Y5 Z10\nM155\nM140 S210");

            using StreamReader reader = GetStreamReader(sim);

            Assert.False(reader.EndOfStream);
            Assert.Equal("ok", reader.ReadLine());
        }

        private static StreamWriter GetStreamWriter(SerialPrinterStreamSimulator sim)
        {
            return new StreamWriter(sim, Encoding.ASCII, 1024, true)
            {
                AutoFlush = true,
                NewLine = "\n",
            };
        }

        private static StreamReader GetStreamReader(SerialPrinterStreamSimulator sim)
        {
            return new StreamReader(sim, Encoding.ASCII, false, 1024, true);
        }
    }
}
