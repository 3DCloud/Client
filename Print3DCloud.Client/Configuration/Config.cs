using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

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
        public static JsonSerializerOptions Options => new JsonSerializerOptions
        {
            WriteIndented = true,
            IgnoreReadOnlyProperties = false,
        };

        /// <summary>
        /// Gets the path of the configuration file.
        /// </summary>
        public static string FilePath => Path.Join(Directory.GetCurrentDirectory(), "config.json");

        /// <summary>
        /// Gets the client's GUID.
        /// </summary>
        [JsonInclude]
        public Guid Guid { get; private set; } = Guid.NewGuid();

        /// <summary>
        /// Gets or sets the key used to authenticate with the server.
        /// </summary>
        public string? Key { get; set; }

        /// <summary>
        /// Loads the configuration from disk as an asynchronous task.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>The loaded configuration or an empty configuration if the file failed to load.</returns>
        public static async Task<Config> LoadAsync(CancellationToken cancellationToken)
        {
            Config? config = null;

            if (File.Exists(FilePath))
            {
                try
                {
                    await using (FileStream fileStream = File.OpenRead(FilePath))
                    {
                        config = await JsonSerializer.DeserializeAsync<Config>(fileStream, Options, cancellationToken);
                    }
                }
                catch (JsonException ex)
                {
                    Console.WriteLine(ex);
                }
            }

            if (config == null)
            {
                config = new Config();
            }

            return config;
        }

        /// <summary>
        /// Saves the current configuration to disk as an asynchronous task.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the file has been saved.</returns>
        public async Task SaveAsync(CancellationToken cancellationToken)
        {
            await using (FileStream fileStream = File.OpenWrite(FilePath))
            {
                await JsonSerializer.SerializeAsync(fileStream, this, Options, cancellationToken);
            }
        }
    }
}
