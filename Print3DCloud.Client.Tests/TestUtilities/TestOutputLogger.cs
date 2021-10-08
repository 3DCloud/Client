using System;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Print3DCloud.Client.Tests.TestUtilities
{
    public class TestOutputLogger<T> : ILogger<T>
    {
        private readonly ITestOutputHelper testOutputHelper;

        public TestOutputLogger(ITestOutputHelper testOutputHelper)
        {
            this.testOutputHelper = testOutputHelper;
        }

        /// <inheritdoc/>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            this.testOutputHelper.WriteLine($"{GetLogLevelShortString(logLevel)} {typeof(T).FullName}[{eventId}]\n      {formatter(state, exception)}");
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
                    return "err: ";
                case LogLevel.Critical:
                    return "crit:";
                default:
                    return "     ";
            }
        }
    }
}