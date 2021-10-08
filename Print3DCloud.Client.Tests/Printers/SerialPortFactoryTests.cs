using System.IO.Ports;
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

            Assert.IsType<SerialPortWrapper>(serialPort);

            SerialPortWrapper serialPortWrapper = (SerialPortWrapper)serialPort;
            Assert.False(serialPortWrapper.IsOpen);
            Assert.Equal("portname", serialPortWrapper.PortName);
            Assert.Equal(123_456, serialPortWrapper.BaudRate);
            Assert.False(serialPortWrapper.RtsEnable);
            Assert.True(serialPortWrapper.DtrEnable);
            Assert.Equal(2_000, serialPortWrapper.WriteTimeout);
        }
    }
}
