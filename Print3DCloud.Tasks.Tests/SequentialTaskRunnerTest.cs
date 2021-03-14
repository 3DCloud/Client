using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Print3DCloud.Tasks.Tests
{
    public class SequentialTaskRunnerTest
    {
        [Fact]
        public async Task Enqueue_Task_Runs()
        {
            // Arrange
            var taskRunner = new SequentialTaskRunner();

            // Act
            await taskRunner.Enqueue(() => Task.Delay(100));
        }

        [Fact]
        public async Task Enqueue_TaskWithReturnValue_Runs()
        {
            // Arrange
            var taskRunner = new SequentialTaskRunner();

            // Act
            int value = await taskRunner.Enqueue(async () =>
            {
                await Task.Delay(100);
                return 25;
            });

            // Assert
            Assert.Equal(25, value);
        }

        [Fact]
        public async Task Enqueue_CanceledTask_RunsAndThrowsException()
        {
            // Arrange
            var taskRunner = new SequentialTaskRunner();
            var cancellationTokenSource = new CancellationTokenSource();

            cancellationTokenSource.CancelAfter(100);

            // Act
            await Assert.ThrowsAsync<TaskCanceledException>(() => taskRunner.Enqueue(() => Task.Delay(500, cancellationTokenSource.Token)));
        }

        [Fact]
        public async Task Enqueue_CanceledGenericTask_RunsAndThrowsException()
        {
            // Arrange
            var taskRunner = new SequentialTaskRunner();
            var cancellationTokenSource = new CancellationTokenSource();

            cancellationTokenSource.CancelAfter(100);

            // Act
            await Assert.ThrowsAsync<TaskCanceledException>(() => taskRunner.Enqueue(async () =>
            {
                await Task.Delay(500, cancellationTokenSource.Token);
                return 10;
            }));
        }

        [Fact]
        public async Task Enqueue_TaskCanceledBeforeRunning_IsCanceledAndDoesNotRun()
        {
            // Arrange
            var taskRunner = new SequentialTaskRunner();
            var cancellationTokenSource = new CancellationTokenSource();

            cancellationTokenSource.CancelAfter(100);

            _ = taskRunner.Enqueue(() => Task.Delay(Timeout.Infinite, cancellationTokenSource.Token));

            // Act
            await Assert.ThrowsAsync<TaskCanceledException>(() => taskRunner.Enqueue(() => Task.FromException(new Exception("If this is thrown, the task has run when it shouldn't have")), cancellationTokenSource.Token));
        }

        [Fact]
        public async Task Enqueue_GenericTaskCanceledBeforeRunning_IsCanceledAndDoesNotRun()
        {
            // Arrange
            var taskRunner = new SequentialTaskRunner();
            var cancellationTokenSource = new CancellationTokenSource();

            cancellationTokenSource.CancelAfter(100);

            _ = taskRunner.Enqueue(() => Task.Delay(Timeout.Infinite, cancellationTokenSource.Token));

            // Act
            await Assert.ThrowsAsync<TaskCanceledException>(() => taskRunner.Enqueue(() => Task.FromException<int>(new Exception("If this is thrown, the task has run when it shouldn't have")), cancellationTokenSource.Token));
        }

        [Fact]
        public async Task Enqueue_TaskWithException_RunsAndThrowsException()
        {
            // Arrange
            var taskRunner = new SequentialTaskRunner();

            // Act
            await Assert.ThrowsAsync<InvalidOperationException>(() => taskRunner.Enqueue(() =>
            {
                return Task.FromException(new InvalidOperationException());
            }));
        }

        [Fact]
        public async Task Enqueue_GenericTaskWithException_RunsAndThrowsException()
        {
            // Arrange
            var taskRunner = new SequentialTaskRunner();

            // Act
            await Assert.ThrowsAsync<InvalidOperationException>(() => taskRunner.Enqueue(() =>
            {
                return Task.FromException<int>(new InvalidOperationException());
            }));
        }
    }
}
