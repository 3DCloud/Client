using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Print3DCloud.Client
{
    /// <summary>
    /// Contains properties related to the logging behavior of this assembly.
    /// </summary>
    public class Logging
    {
        /// <summary>
        /// Gets or sets the <see cref="ILoggerFactory"/> that this assembly will use.
        /// </summary>
        public static ILoggerFactory LoggerFactory { get; set; } = new NullLoggerFactory();
    }
}
