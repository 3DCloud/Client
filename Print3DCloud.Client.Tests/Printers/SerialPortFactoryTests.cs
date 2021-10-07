using Print3DCloud.Client.Printers;
using Xunit;

namespace Print3DCloud.Client.Tests.Printers
{
    public class SerialPortFactoryTests
    {
        [Fact]
        public void CreateSerialPort_WithValidArguments_CreatesSerialPortInstance()
        {
            SerialPortFactory serialPortFactory = new();

            ISerialPort serialPort = serialPortFactory.CreateSerialPort("portname", 123_456);

            Assert.False(serialPort.IsOpen);
            Assert.Equal("portname", serialPort.PortName);
            Assert.Equal(123_456, serialPort.BaudRate);
            Assert.False(serialPort.RtsEnable);
            Assert.True(serialPort.DtrEnable);
        }
    }
}
