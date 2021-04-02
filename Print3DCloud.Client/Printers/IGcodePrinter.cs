using System.Threading;
using System.Threading.Tasks;

namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// Represents a connected 3D printer that is controlled via Gcode.
    /// </summary>
    internal interface IGcodePrinter : IPrinter
    {
        /// <summary>
        /// Sends a G-code command to the printer.
        /// </summary>
        /// <param name="command">The command to send.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the command has been sent.</returns>
        Task SendCommandAsync(string command, CancellationToken cancellationToken);

        /// <summary>
        /// Sends a block of G-code commands to the printer.
        /// </summary>
        /// <param name="commands">The commands to send, separated by a new line.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the commands have been sent.</returns>
        Task SendCommandBlockAsync(string commands, CancellationToken cancellationToken);
    }
}
