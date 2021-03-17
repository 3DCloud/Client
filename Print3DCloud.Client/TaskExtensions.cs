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

            return task.ContinueWith(t => Task.CompletedTask, TaskContinuationOptions.RunContinuationsAsynchronously).Unwrap();
        }
    }
}
