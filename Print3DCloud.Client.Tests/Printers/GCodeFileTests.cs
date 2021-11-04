using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Print3DCloud.Client.Printers;
using Xunit;

namespace Print3DCloud.Client.Tests.Printers
{
    public class GCodeFileTests
    {
        public static IEnumerable<object[]> FileData => new[]
        {
            new object[]
            {
                "Fixtures/Files/SampleGCode/Marvin_Cura_4.11_GenericMarlin_Dual.gcode",
                "Marlin",
                67,
                new MaterialAmount(24.2886, MaterialAmountType.Length),
                new MaterialAmount(5.10839, MaterialAmountType.Length),
            },
            new object[]
            {
                "Fixtures/Files/SampleGCode/Marvin_Cura_4.11_UltiGCode_Dual.gcode",
                "UltiGCode",
                65,
                new MaterialAmount(154, MaterialAmountType.Volume),
                new MaterialAmount(32, MaterialAmountType.Volume),
            },
            new object[]
            {
                "Fixtures/Files/SampleGCode/Marvin_Cura_4.11_VolumetricMarlin.gcode",
                "Marlin(Volumetric)",
                54,
                new MaterialAmount(154.947, MaterialAmountType.Volume),
            },
            new object[]
            {
                "Fixtures/Files/SampleGCode/Marvin_Cura_4.11_VolumetricMarlin_Dual.gcode",
                "Marlin(Volumetric)",
                65,
                new MaterialAmount(154.947, MaterialAmountType.Volume),
                new MaterialAmount(32.5885, MaterialAmountType.Volume),
            },
        };

        [Theory]
        [MemberData(nameof(FileData))]
        public async Task PreprocessAsync_ParsesHeaderData(string filePath, string flavor, int totalTime, params MaterialAmount[] materialAmounts)
        {
            GCodeFile file = new(File.OpenRead(filePath));

            await file.PreprocessAsync(CancellationToken.None);

            Assert.Equal(flavor, file.Flavor);
            Assert.Equal(totalTime, file.TotalTime);
            Assert.Collection(
                file.MaterialAmounts, materialAmounts.Select<MaterialAmount, Action<MaterialAmount>>((ma) =>
                {
                    return (val) =>
                    {
                        Assert.Equal(ma.Amount, val.Amount);
                        Assert.Equal(ma.Type, val.Type);
                    };
                }).ToArray());
        }

        [Fact]
        public async Task GetAsyncEnumerator_ValidGCode_IteratesOverCommands()
        {
            GCodeFile file = new(new MemoryStream(Encoding.UTF8.GetBytes(";FLAVOR:Marlin\nG28 ; home axes\nG0 X5 Y5 ; move to front left\nM104 S210 ; heat up extruder")));

            IAsyncEnumerator<string> commands = file.GetAsyncEnumerator(CancellationToken.None);

            Assert.True(await commands.MoveNextAsync());
            Assert.Equal("G28", commands.Current);

            Assert.True(await commands.MoveNextAsync());
            Assert.Equal("G0 X5 Y5", commands.Current);

            Assert.True(await commands.MoveNextAsync());
            Assert.Equal("M104 S210", commands.Current);

            Assert.False(await commands.MoveNextAsync());
        }
    }
}