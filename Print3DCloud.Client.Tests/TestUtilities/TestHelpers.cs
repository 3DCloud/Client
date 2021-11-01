using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit.Abstractions;

namespace Print3DCloud.Client.Tests.TestUtilities
{
    /// <summary>
    /// Contains various methods that are useful across multiple test suites.
    /// </summary>
    public static class TestHelpers
    {
        /// <summary>
        /// Get a token that times out after a given number of milliseconds.
        /// </summary>
        /// <param name="millisecondsDelay">The number of milliseconds after which the token will be canceled.</param>
        /// <returns>A token that gets cancelled after the specified number of milliseconds.</returns>
        public static CancellationToken CreateTimeOutToken(int millisecondsDelay = 2000) => new CancellationTokenSource(millisecondsDelay).Token;

        /// <summary>
        /// Create a mock <see cref="ILogger{TCategoryName}"/>.
        /// </summary>
        /// <typeparam name="T">The type whose name is used for the logger category name.</typeparam>
        /// <param name="testOutputHelper">The test output helper to which messages should be passed.</param>
        /// <returns>An instance implementing <see cref="ILogger{TCategoryName}"/>.</returns>
        public static ILogger<T> CreateLogger<T>(ITestOutputHelper? testOutputHelper)
        {
            if (testOutputHelper != null)
            {
                return new TestOutputLogger<T>(testOutputHelper);
            }
            else
            {
                return new NullLogger<T>();
            }
        }
    }
}
