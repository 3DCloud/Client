using System;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Print3DCloud.Client.Tests.TestUtilities
{
    /// <summary>
    /// Sends logs to xUnit output.
    /// </summary>
    /// <typeparam name="TCategoryName">The type who's name is used for the logger category name.</typeparam>
    public class TestOutputLogger<TCategoryName> : ILogger<TCategoryName>
    {
        private readonly ITestOutputHelper testOutputHelper;

        /// <summary>
        /// Initializes a new instance of the <see cref="TestOutputLogger{TCategoryName}"/> class.
        /// </summary>
        /// <param name="testOutputHelper">The <see cref="ITestOutputHelper"/> to which logs will be sent.</param>
        public TestOutputLogger(ITestOutputHelper testOutputHelper)
        {
            this.testOutputHelper = testOutputHelper;
        }

        /// <inheritdoc/>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            this.testOutputHelper.WriteLine($"{GetLogLevelShortString(logLevel)} {typeof(TCategoryName).FullName}[{eventId}]\n      {formatter(state, exception)}");
        }

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        /// <inheritdoc/>
        public IDisposable BeginScope<TState>(TState state)
        {
            throw new NotImplementedException();
        }

        private static string GetLogLevelShortString(LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.Trace:
                    return "trce:";
                case LogLevel.Debug:
                    return "dbug:";
                case LogLevel.Information:
                    return "info:";
                case LogLevel.Warning:
                    return "warn:";
                case LogLevel.Error:
                    return "fail:";
                case LogLevel.Critical:
                    return "crit:";
                default:
                    return "     ";
            }
        }
    }
}