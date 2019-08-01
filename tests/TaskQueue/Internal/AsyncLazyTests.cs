using System.Diagnostics;
using System.Threading.Tasks;
using FluentAssertions;
using Sceny.Internal;
using Xunit;

namespace Sceny.Tests.Internal
{
    public class AsyncLazyTests
    {
        [Fact]
        public void Get_long_running_task_does_not_blocks()
        {
            // arrange
            const int crazyLongerWaiting = 10000;
            var stopWatch = new Stopwatch();
            stopWatch.Start();
            var somethingGotExecuted = false;
            // act
            var lazyBool = new AsyncLazy<bool>(async ct =>
            {
                somethingGotExecuted = true; // Before the task awaiting it will be sync
                await Task.Delay(crazyLongerWaiting);
                return true;
            });
            _ = lazyBool.Value; // just trigger the lazy initialization, do not wait for it
            // assert
            stopWatch.Stop();
            somethingGotExecuted.Should().BeTrue();
            stopWatch.ElapsedMilliseconds.Should().BeLessThan(10); // any kind of small number
        }

        [Fact]
        public async Task Waiting_value()
        {
            // arrange
            // ...
            // act
            var lazyBool = new AsyncLazy<bool>(async ct =>
            {
                await Task.Yield(); // yield and go async
                return true;
            });
            var @bool = await lazyBool.Value;
            // assert
            @bool.Should().BeTrue();
        }
    }
}