using System.Threading;
using System.Threading.Tasks;

namespace Print3DCloud.Client
{
    /// <summary>
    /// Extension methods related to the <see cref="Task"/> class.
    /// </summary>
    internal static class TaskExtensions
    {
        /// <summary>
        /// Waits for a <see cref="AutoResetEvent"/> to signal as an asynchronous task.
        /// </summary>
        /// <param name="autoResetEvent">The <see cref="AutoResetEvent"/> instance.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes when the event is signalled.</returns>
        public static Task WaitOneAsync(this AutoResetEvent autoResetEvent, CancellationToken cancellationToken)
        {
            if (autoResetEvent.WaitOne(0))
            {
                return Task.CompletedTask;
            }

            var completionSource = new TaskCompletionSource();
            cancellationToken.Register(() => completionSource.TrySetCanceled());

            ThreadPool.RegisterWaitForSingleObject(autoResetEvent, (_, _) => completionSource.TrySetResult(), null, Timeout.Infinite, true);

            return completionSource.Task;
        }
    }
}
