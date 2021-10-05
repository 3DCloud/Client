using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Print3DCloud.Client.Tests.TestUtilities
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
            Assert.Equal("start", reader.ReadLine());
            Assert.True(reader.EndOfStream);
        }

        [Fact]
        public void RespondTo_WithMessageSentAlone_Responds()
        {
            using SerialPrinterStreamSimulator sim = new();

            sim.RegisterResponse("M155", "ok");

            using (StreamWriter writer = GetStreamWriter(sim))
            {
                writer.WriteLine("M155");
            }

            using StreamReader reader = GetStreamReader(sim);

            Assert.Equal("ok", reader.ReadLine());
            Assert.True(reader.EndOfStream);
        }

        [Fact]
        public void RespondTo_WithMessageSentInParts_Responds()
        {
            using SerialPrinterStreamSimulator sim = new();

            sim.RegisterResponse("M155", "ok");

            using (StreamWriter writer = GetStreamWriter(sim))
            {
                writer.Write("M1");
                writer.Write("55\n");
            }

            using StreamReader reader = GetStreamReader(sim);

            Assert.Equal("ok", reader.ReadLine());
            Assert.True(reader.EndOfStream);
        }

        [Fact]
        public void RespondTo_WithMessageSentWithOtherMessages_Responds()
        {
            using SerialPrinterStreamSimulator sim = new();

            sim.RegisterResponse("M155", "ok");

            using (StreamWriter writer = GetStreamWriter(sim))
            {
                writer.WriteLine("G0 X0 Y5 Z10\nM155\nM140 S210");
            }

            using StreamReader reader = GetStreamReader(sim);

            Assert.Equal("ok", reader.ReadLine());
            Assert.True(reader.EndOfStream);
        }

        [Fact]
        public void RespondTo_WithResponsesThatHaveTimes_RespondsInOrder()
        {
            using SerialPrinterStreamSimulator sim = new();

            sim.RegisterResponse("M155", "ok", 1);
            sim.RegisterResponse("M155", "no", 1);

            using (StreamWriter writer = GetStreamWriter(sim))
            {
                writer.WriteLine("M155");
                writer.WriteLine("M155");
            }

            using StreamReader reader = GetStreamReader(sim);

            Assert.Equal("ok", reader.ReadLine());
            Assert.Equal("no", reader.ReadLine());
            Assert.True(reader.EndOfStream);
        }

        [Fact]
        public void RespondTo_WithResponseThatHasSubstitutions_FillsThemIn()
        {
            using SerialPrinterStreamSimulator sim = new();

            sim.RegisterResponse(new Regex(@"M155 (\d+)"), "ok $1", 1);

            using (StreamWriter writer = GetStreamWriter(sim))
            {
                writer.WriteLine("M155 1234");
            }

            using StreamReader reader = GetStreamReader(sim);

            Assert.Equal("ok 1234", reader.ReadLine());
            Assert.True(reader.EndOfStream);
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
