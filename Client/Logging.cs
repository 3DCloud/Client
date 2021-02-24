using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Client
{
    public class Logging
    {
        public static ILoggerFactory LoggerFactory = new NullLoggerFactory();
    }
}
