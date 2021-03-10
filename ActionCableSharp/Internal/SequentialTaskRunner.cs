﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ActionCableSharp.Internal
{
    /// <summary>
    /// Queues tasks and runs them one by one sequentially.
    /// </summary>
    internal class SequentialTaskRunner
    {
        private readonly ConcurrentQueue<SequentialTask> taskQueue = new ConcurrentQueue<SequentialTask>();
        private Task? currentTask;

        /// <summary>
        /// Enqueue a <see cref="Task"/>.
        /// </summary>
        /// <param name="task">The <see cref="Task"/> to enqueue.</param>
        /// <returns>A task that finishes once the queued task has run.</returns>
        public Task Enqueue(Func<Task> task)
        {
            var tcs = new TaskCompletionSource();

            this.taskQueue.Enqueue(new VoidSequentialTask(task, tcs));

            this.StartNext();

            return tcs.Task;
        }

        /// <summary>
        /// Enqueue a <see cref="Task{T}"/>.
        /// </summary>
        /// <typeparam name="T">The result type of the task.</typeparam>
        /// <param name="task">The <see cref="Task{T}"/> to enqueue.</param>
        /// <returns>A task that finishes once the queued task has run.</returns>
        public Task<T> Enqueue<T>(Func<Task<T>> task)
        {
            var tcs = new TaskCompletionSource<T>();

            this.taskQueue.Enqueue(new GenericSequentialTask<T>(task, tcs));

            this.StartNext();

            return tcs.Task;
        }

        /// <summary>
        /// Starts the task that's next in queue. Does nothing if there are no tasks to run.
        /// </summary>
        private void StartNext()
        {
            if (this.currentTask != null) return;
            if (!this.taskQueue.TryDequeue(out SequentialTask? item)) return;

            this.currentTask = item.Run().ContinueWith(
                (task, state) =>
            {
                this.currentTask = null;
                this.StartNext();
            }, TaskContinuationOptions.None);
        }

        private abstract class SequentialTask
        {
            private readonly Func<Task> func;

            protected SequentialTask(Func<Task> func)
            {
                this.func = func;
            }

            public Task Run()
            {
                Task task = this.func();

                task.ContinueWith(
                    (task, state) =>
                {
                    if (task.IsCompletedSuccessfully)
                    {
                        this.SetResult(task);
                    }
                    else if (task.IsFaulted)
                    {
                        this.SetException(task.Exception!.InnerExceptions);
                    }
                    else if (task.IsCanceled)
                    {
                        this.SetCanceled();
                    }
                }, TaskContinuationOptions.None);

                return task;
            }

            protected abstract void SetResult(Task task);

            protected abstract void SetCanceled();

            protected abstract void SetException(IEnumerable<Exception> exception);
        }

        private class VoidSequentialTask : SequentialTask
        {
            private readonly TaskCompletionSource taskCompletionSource;

            public VoidSequentialTask(Func<Task> func, TaskCompletionSource taskCompletionSource)
                : base(func)
            {
                this.taskCompletionSource = taskCompletionSource;
            }

            protected override void SetResult(Task task)
            {
                this.taskCompletionSource.SetResult();
            }

            protected override void SetCanceled()
            {
                this.taskCompletionSource.SetCanceled();
            }

            protected override void SetException(IEnumerable<Exception> exception)
            {
                this.taskCompletionSource.SetException(exception);
            }
        }

        private class GenericSequentialTask<T> : SequentialTask
        {
            private readonly TaskCompletionSource<T> taskCompletionSource;

            public GenericSequentialTask(Func<Task<T>> func, TaskCompletionSource<T> taskCompletionSource)
                : base(func)
            {
                this.taskCompletionSource = taskCompletionSource;
            }

            protected override void SetResult(Task task)
            {
                this.taskCompletionSource.SetResult(((Task<T>)task).Result);
            }

            protected override void SetCanceled()
            {
                this.taskCompletionSource.SetCanceled();
            }

            protected override void SetException(IEnumerable<Exception> exception)
            {
                this.taskCompletionSource.SetException(exception);
            }
        }
    }
}
