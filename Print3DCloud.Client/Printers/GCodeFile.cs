using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// Represents a G-code file.
    /// </summary>
    public class GCodeFile : IAsyncEnumerable<string>, IDisposable
    {
        private readonly Stream stream;

        /// <summary>
        /// Initializes a new instance of the <see cref="GCodeFile"/> class.
        /// </summary>
        /// <param name="stream">Stream containing the G-code.</param>
        public GCodeFile(Stream stream)
        {
            if (!stream.CanRead)
            {
                throw new InvalidOperationException("Stream must support reading");
            }

            if (!stream.CanSeek)
            {
                throw new InvalidOperationException("Stream must support seeking");
            }

            this.stream = stream;
        }

        /// <summary>
        /// Gets the G-code flavor.
        /// </summary>
        public string? Flavor { get; private set; }

        /// <summary>
        /// Gets the total print time, in seconds.
        /// </summary>
        public int TotalTime { get; private set; }

        /// <summary>
        /// Gets the progress steps detected in the file, if any.
        /// </summary>
        public List<ProgressTimeStep> ProgressSteps { get; private set; } = new(0);

        /// <summary>
        /// Gets the material amounts detected in the file, if any.
        /// </summary>
        public List<MaterialAmount> MaterialAmounts { get; private set; } = new(0);

        /// <summary>
        /// Reads through the file and extracts information related to the print.
        /// </summary>
        /// <returns>A <see cref="Task"/> that completes once the file has been processed.</returns>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        public async Task PreprocessAsync(CancellationToken cancellationToken)
        {
            this.stream.Position = 0;

            List<ProgressTimeStep> steps = new();
            List<MaterialAmount> materialAmounts = new();

            using StreamReader reader = new(this.stream, null, true, -1, true);
            {
                while (!reader.EndOfStream)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string? line = await reader.ReadLineAsync();

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    line = line.Trim();

                    if (line.StartsWith(';') && line.Contains(':'))
                    {
                        string[] parts = line[1..].Split(':', 2);
                        string key = parts[0].Trim();
                        string value = parts[1].Trim();

                        switch (key)
                        {
                            // Cura
                            case "FLAVOR":
                                this.Flavor = value;
                                break;

                            // Cura (UltiGCode)
                            case "MATERIAL":
                                materialAmounts.Insert(0, new MaterialAmount(int.Parse(value, CultureInfo.InvariantCulture), MaterialAmountType.Volume));
                                break;

                            // Cura (UltiGCode)
                            case "MATERIAL2":
                                materialAmounts.Insert(1, new MaterialAmount(int.Parse(value, CultureInfo.InvariantCulture), MaterialAmountType.Volume));
                                break;

                            // Cura (Marlin)
                            case "Filament used":
                                materialAmounts = value.Split(',').Select((s) =>
                                {
                                    s = s.Trim();
                                    double amount = double.Parse(new string(s.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()), CultureInfo.InvariantCulture);
                                    string unit = new string(s.SkipWhile(c => char.IsDigit(c) || c == '.').ToArray()).Trim();
                                    MaterialAmountType type;

                                    switch (unit)
                                    {
                                        case "mm3":
                                            type = MaterialAmountType.Volume;
                                            break;

                                        case "m":
                                            type = MaterialAmountType.Length;
                                            amount *= 1000;
                                            break;

                                        default:
                                            type = MaterialAmountType.Length;
                                            break;
                                    }

                                    return new MaterialAmount(amount, type);
                                }).ToList();
                                break;

                            // Cura
                            case "PRINT.TIME":
                            case "TIME":
                                this.TotalTime = int.Parse(value);
                                steps.Add(new ProgressTimeStep(0, this.TotalTime));
                                break;

                            // Cura
                            case "TIME_ELAPSED":
                                steps.Add(new ProgressTimeStep(this.stream.Position, this.TotalTime - (int)double.Parse(value, CultureInfo.InvariantCulture)));
                                break;

                            // ideaMaker
                            case "PRINTING_TIME":
                                this.TotalTime = Math.Max(this.TotalTime, int.Parse(value, CultureInfo.InvariantCulture));
                                break;

                            // ideaMaker
                            case "REMAINING_TIME":
                                steps.Add(new ProgressTimeStep(this.stream.Position, int.Parse(value, CultureInfo.InvariantCulture)));
                                break;
                        }
                    }
                }
            }

            steps.TrimExcess();
            this.ProgressSteps = steps;
            this.MaterialAmounts = materialAmounts;
        }

        /// <summary>
        /// Iterates over the G-code commands in the file. Commands are returned sanitized (comments removed).
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>An <see cref="IAsyncEnumerator{T}"/> that iterates over the G-code commands in the file.</returns>
        public async IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken cancellationToken)
        {
            this.stream.Position = 0;

            using StreamReader reader = new(this.stream, null, true, -1, true);

            while (!reader.EndOfStream)
            {
                string? line = await reader.ReadLineAsync();

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                line = line.Trim();

                int index = line.IndexOf(';');

                if (index == 0)
                {
                    continue;
                }
                else if (index != -1)
                {
                    line = line[..index].Trim();
                }

                yield return line;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.stream.Dispose();
            }
        }
    }
}