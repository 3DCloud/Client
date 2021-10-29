using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Events;

namespace Print3DCloud.Client.Configuration
{
    /// <summary>
    /// The global configuration for a 3DCloud client.
    /// </summary>
    internal class Config
    {
        /// <summary>
        /// Gets the <see cref="JsonSerializerOptions"/> used when loading and saving the configuration.
        /// </summary>
        public static JsonSerializerOptions Options => new()
        {
            WriteIndented = true,
            IgnoreReadOnlyProperties = false,
            Converters =
            {
                new JsonStringEnumConverter(),
            },
        };

        /// <summary>
        /// Gets the path of the configuration file.
        /// </summary>
        public static string FilePath => Path.Join(Directory.GetCurrentDirectory(), "config.json");

        /// <summary>
        /// Gets the server host (domain name + port).
        /// </summary>
        [JsonInclude]
        public string? ServerHost { get; private set; }

        /// <summary>
        /// Gets the client's GUID.
        /// </summary>
        [JsonInclude]
        public Guid ClientId { get; private set; } = Guid.NewGuid();

        /// <summary>
        /// Gets the client's secret.
        /// </summary>
        [JsonInclude]
        public string Secret { get; private set; } = GenerateRandomBase64String();

        /// <summary>
        /// Gets the secret token to use when communicating with Rollbar for error reporting.
        /// </summary>
        [JsonInclude]
        public string? RollbarAccessToken { get; private set; }

        /// <summary>
        /// Gets the log level to use when logging messages to console.
        /// </summary>
        [JsonInclude]
        public LogEventLevel ConsoleLogLevel { get; private set; } = LogEventLevel.Debug;

        /// <summary>
        /// Gets the log level to use when logging messages to console.
        /// </summary>
        [JsonInclude]
        public LogEventLevel RollbarLogLevel { get; private set; } = LogEventLevel.Information;

        /// <summary>
        /// Loads the configuration from disk as an asynchronous task.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>The loaded configuration or an empty configuration if the file failed to load.</returns>
        public static async Task<Config> LoadAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(FilePath))
            {
                return new Config();
            }

            await using FileStream fileStream = File.OpenRead(FilePath);
            return await JsonSerializer.DeserializeAsync<Config>(fileStream, Options, cancellationToken).ConfigureAwait(false) ?? new Config();
        }

        /// <summary>
        /// Saves the current configuration to disk as an asynchronous task.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the file has been saved.</returns>
        public async Task SaveAsync(CancellationToken cancellationToken)
        {
            await using FileStream fileStream = File.OpenWrite(FilePath);
            await JsonSerializer.SerializeAsync(fileStream, this, Options, cancellationToken).ConfigureAwait(false);
        }

        private static string GenerateRandomBase64String()
        {
            byte[] bytes = new byte[36];
            RandomNumberGenerator.Create().GetBytes(bytes);
            return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_');
        }
    }
}
