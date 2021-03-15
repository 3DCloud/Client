using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Print3DCloud.Tasks.Tests
{
    public class TaskExtensionsTest
    {
        [Fact]
        public async Task ForEachAsync_RunInParallel_ProducesItems()
        {
            // Arrange
            var items = new[] { "yes", "hello", "hi" };
            var copy = new List<string>();

            // Act
            await items.ForEachAsync(
                async (str, ct) =>
                {
                    await Task.Yield();
                    copy.Add(str);
                },
                CancellationToken.None,
                1);

            // Assert
            Assert.Equal(items, copy.OrderBy(item => Array.IndexOf(items, item)));
        }

        [Fact]
        public async Task ForEachAsync_RunSequentially_ProducesOrderedItems()
        {
            // Arrange
            var items = new[] { "yes", "hello", "hi" };
            var copy = new List<string>();

            // Act
            await items.ForEachAsync(
                async (str, ct) =>
                {
                    await Task.Yield();
                    copy.Add(str);
                },
                CancellationToken.None,
                1);

            // Assert
            Assert.Equal(items, copy);
        }
    }
}
