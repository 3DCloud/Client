using Xunit;

namespace ActionCableSharp.Tests
{
    public class SnakeCaseNamingPolicyTest
    {
        [Theory]
        [InlineData("Property", "property")]
        [InlineData("PascalCase", "pascal_case")]
        [InlineData("lowercase", "lowercase")]
        [InlineData("camelCase", "camel_case")]
        [InlineData("AcronymURITest", "acronym_uri_test")]
        [InlineData("propertyWithP", "property_with_p")]
        public void ConvertName_ValidInput_ProducesValidOutput(string propertyName, string expectedOutput)
        {
            // Arrange
            var namingPolicy = new SnakeCaseNamingPolicy();

            // Act
            string result = namingPolicy.ConvertName(propertyName);

            // Assert
            Assert.Equal(expectedOutput, result);
        }
    }
}
