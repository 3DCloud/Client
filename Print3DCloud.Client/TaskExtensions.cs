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
        /// Waits for the specified <see cref="Task"/> to complete, ignoring the result or any exceptions.
        /// </summary>
        /// <param name="task">The target <see cref="Task"/>.</param>
        /// <returns>A <see cref="Task"/> that completes on a separate thread when the specified task is completed.</returns>
        public static Task WaitForCompletionAsync(this Task task)
        {
            if (task.IsCompleted)
            {
                return Task.CompletedTask;
            }

            return task.ContinueWith(t => { }, TaskContinuationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Waits for a <see cref="AutoResetEvent"/> to signal as an asynchronous task.
        /// </summary>
        /// <param name="manualResetEvent">The <see cref="AutoResetEvent"/> instance.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes when the event is signalled.</returns>
        public static Task WaitOneAsync(this AutoResetEvent manualResetEvent, CancellationToken cancellationToken)
        {
            if (manualResetEvent.WaitOne(0))
            {
                return Task.CompletedTask;
            }

            var completionSource = new TaskCompletionSource();
            cancellationToken.Register(() => completionSource.TrySetCanceled());

            ThreadPool.RegisterWaitForSingleObject(manualResetEvent, (state, timedOut) => completionSource.TrySetResult(), null, Timeout.Infinite, true);

            return completionSource.Task;
        }
    }
}
