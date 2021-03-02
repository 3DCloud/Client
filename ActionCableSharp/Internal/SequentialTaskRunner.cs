using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ActionCableSharp.Internal
{
    internal class SequentialTaskRunner
    {
        private readonly ConcurrentQueue<SequentialTask> taskQueue = new ConcurrentQueue<SequentialTask>();
        private Task? currentTask;

        public Task Enqueue(Func<Task> task)
        {
            var tcs = new TaskCompletionSource();

            taskQueue.Enqueue(new VoidSequentialTask(task, tcs));

            StartNext();

            return tcs.Task;
        }

        public Task<T> Enqueue<T>(Func<Task<T>> task)
        {
            var tcs = new TaskCompletionSource<T>();

            taskQueue.Enqueue(new GenericSequentialTask<T>(task, tcs));

            StartNext();

            return tcs.Task;
        }

        private void StartNext()
        {
            if (currentTask != null) return;
            if (!taskQueue.TryDequeue(out SequentialTask? item)) return;

            currentTask = item.Run().ContinueWith((task, state) =>
            {
                currentTask = null;
                StartNext();
            }, TaskContinuationOptions.None);
        }

        private abstract class SequentialTask
        {
            private Func<Task> func;

            protected SequentialTask(Func<Task> func)
            {
                this.func = func;
            }

            public Task Run()
            {
                Task task = func();

                task.ContinueWith((task, state) =>
                {
                    if (task.IsCompletedSuccessfully)
                    {
                        SetResult(task);
                    }
                    else if (task.IsFaulted)
                    {
                        SetException(task.Exception!.InnerExceptions);
                    }
                    else if (task.IsCanceled)
                    {
                        SetCanceled();
                    }
                }, TaskContinuationOptions.None);

                return task;
            }

            public abstract void SetResult(Task task);
            public abstract void SetCanceled();
            public abstract void SetException(IEnumerable<Exception> exception);
        }

        private class VoidSequentialTask : SequentialTask
        {
            private TaskCompletionSource taskCompletionSource;

            public VoidSequentialTask(Func<Task> func, TaskCompletionSource taskCompletionSource) : base(func)
            {
                this.taskCompletionSource = taskCompletionSource;
            }

            public override void SetResult(Task task)
            {
                taskCompletionSource.SetResult();
            }

            public override void SetCanceled()
            {
                taskCompletionSource.SetCanceled();
            }

            public override void SetException(IEnumerable<Exception> exception)
            {
                taskCompletionSource.SetException(exception);
            }
        }

        private class GenericSequentialTask<T> : SequentialTask
        {
            private TaskCompletionSource<T> taskCompletionSource;

            public GenericSequentialTask(Func<Task<T>> func, TaskCompletionSource<T> taskCompletionSource) : base(func)
            {
                this.taskCompletionSource = taskCompletionSource;
            }

            public override void SetResult(Task task)
            {
                taskCompletionSource.SetResult(((Task<T>)task).Result);
            }

            public override void SetCanceled()
            {
                taskCompletionSource.SetCanceled();
            }

            public override void SetException(IEnumerable<Exception> exception)
            {
                taskCompletionSource.SetException(exception);
            }
        }
    }
}
