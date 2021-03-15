using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Print3DCloud.Tasks
{
    /// <summary>
    /// Extension methods that help work with <see cref="Task"/> instances.
    /// </summary>
    public static class TaskExtensions
    {
        /// <summary>
        /// Runs every item in the <see cref="IEnumerable{T}"/> through a task with a maximum number of scheduled tasks at a time.
        /// </summary>
        /// <typeparam name="T">Enumerable item type.</typeparam>
        /// <param name="enumerable">Items with which to run tasks.</param>
        /// <param name="action">Task to run.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <param name="maxDegreeOfParallelism">Maximum number of tasks to schedule at a time.</param>
        /// <returns>A <see cref="Task"/> that completes once all enumerable item tasks have completed.</returns>
        public static async Task ForEachAsync<T>(this IEnumerable<T> enumerable, Func<T, CancellationToken, Task> action, CancellationToken cancellationToken, int maxDegreeOfParallelism = 4)
        {
            var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);
            var tasks = new List<Task>();

            foreach (T item in enumerable)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await semaphore.WaitAsync().ConfigureAwait(false);
                tasks.Add(RunTaskAndRelease(action, semaphore, cancellationToken, item));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private static async Task RunTaskAndRelease<T>(Func<T, CancellationToken, Task> action, SemaphoreSlim semaphore, CancellationToken cancellationToken, T item)
        {
            await action(item, cancellationToken);
            semaphore.Release();
        }
    }
}
