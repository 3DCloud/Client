using System;
using System.Threading;
using System.Threading.Tasks;
using ActionCableSharp.Internal;
using Xunit;

namespace ActionCableSharp.Tests
{
    public class SequentialTaskRunnerTests
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
        public async Task Enqueue_TaskWithException_RunsAndThrowsException()
        {
            // Arrange
            var taskRunner = new SequentialTaskRunner();

            // Act
            await Assert.ThrowsAsync<InvalidOperationException>(() => taskRunner.Enqueue(async () =>
            {
                await Task.Delay(100);
                throw new InvalidOperationException();
            }));
        }
    }
}
