using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActionCableSharp
{
    /// <summary>
    /// Contains properties related to the logging behavior of ActionCableSharp.
    /// </summary>
    public class Logging
    {
        /// <summary>
        /// Gets or sets the <see cref="ILoggerFactory"/> that ActionCableSharp will use.
        /// </summary>
        public static ILoggerFactory LoggerFactory { get; set; } = new NullLoggerFactory();
    }
}
