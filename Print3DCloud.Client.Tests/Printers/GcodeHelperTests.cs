using Print3DCloud.Client.Printers;
using Xunit;

namespace Print3DCloud.Client.Tests.Printers
{
    public class GcodeHelperTests
    {
        [Theory]
        [InlineData("G28 X (inline comment) Y Z; full line comment\n", "G28 X  Y Z")]
        [InlineData("G28 X Y Z\nG0 X0 Y10 Z5", "G28 X Y Z")]
        public void SanitizeGcodeCommand_ValidInput_ValidOutput(string input, string expected)
        {
            Assert.Equal(expected, GcodeHelper.SanitizeGcodeCommand(input));
        }

        [Theory]
        [InlineData("G28 X Y Z\nG0 X0 Y10 Z5", "G28")]
        [InlineData("M104 S210", "M104")]
        [InlineData("; nonsense S210", "")]
        public void GetGcodeCommandCode_ValidInput_ValidOutput(string input, string expected)
        {
            Assert.Equal(expected, GcodeHelper.GetGcodeCommandCode(input));
        }
    }
}