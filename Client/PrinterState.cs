﻿namespace Client
{
    /// <summary>
    /// Represents the state of a printer at a given moment.
    /// </summary>
    internal readonly struct PrinterState
    {
        /// <summary>
        /// Gets a value indicating whether the printer is connected or not.
        /// </summary>
        public bool IsConnected { get; init; }

        /// <summary>
        /// Gets a value indicating whether the printer is printing or not.
        /// </summary>
        public bool IsPrinting { get; init; }

        /// <summary>
        /// Gets the temperature(s) of the printer's hotend(s).
        /// </summary>
        public double[] HotendTemperatures { get; init; }

        /// <summary>
        /// Gets the temperature of the bed. Set to null if the printer does not have a heated build plate.
        /// </summary>
        public double? BedTemperature { get; init; }
    }
}
