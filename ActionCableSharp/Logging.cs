using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActionCableSharp
{
    public class Logging
    {
        public static ILoggerFactory LoggerFactory = new NullLoggerFactory();
    }
}
