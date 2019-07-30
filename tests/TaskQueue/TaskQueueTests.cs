using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Sceny.Tests
{
    public class TaskQueueTests
    {
        [Fact]
        public async Task Basic_sync_task_execution()
        {
            // arrange
            var done = false;
            void DoSomething() => done = true;
            // act
            using (var tasks = new TaskQueue())
            {
                _ = tasks.EnqueueAsync(DoSomething);
                await tasks.DrainOutAndDisposeAsync();
            }
            // assert
            done.Should().BeTrue();
        }

        [Fact]
        public async Task Basic_sync_task_execution_can_be_waited()
        {
            // arrange
            void DoSomething() { };
            // act
            Task somethingTask;
            using (var tasks = new TaskQueue())
            {
                somethingTask = tasks.EnqueueAsync(DoSomething);
                await somethingTask;
                await tasks.DrainOutAndDisposeAsync();
            }
            // assert
            somethingTask.IsCompleted.Should().BeTrue();
        }

        [Fact]
        public async Task Basic_async_task_execution()
        {
            // arrange
            var done = false;
            Task DoSomethingAsync(CancellationToken token)
            {
                done = true;
                return Task.CompletedTask;
            }
            // act
            using (var tasks = new TaskQueue())
            {
                _ = tasks.EnqueueAsync(DoSomethingAsync);
                await tasks.DrainOutAndDisposeAsync();
            }
            // assert
            done.Should().BeTrue();
        }

        [Fact]
        public async Task Basic_async_task_execution_can_be_waited()
        {
            // arrange
            Task DoSomethingAsync(CancellationToken token) => Task.CompletedTask;
            // act
            Task somethingTask;
            using (var tasks = new TaskQueue())
            {
                somethingTask = tasks.EnqueueAsync(DoSomethingAsync);
                await somethingTask;
                await tasks.DrainOutAndDisposeAsync();
            }
            // assert
            somethingTask.IsCompleted.Should().BeTrue();
        }

        [Fact]
        public async Task Do_not_run_task_before_previous_completion()
        {
            // arrange
            var done = false;

            var slowTaskSource = new TaskCompletionSource<bool>();
            Task SlowAsync(CancellationToken token) => slowTaskSource.Task;

            Task DoneAsync(CancellationToken token)
            {
                done = true;
                return Task.CompletedTask;
            }
            // act
            using (var tasks = new TaskQueue())
            {
                _ = tasks.EnqueueAsync(SlowAsync);
                var doneTask = tasks.EnqueueAsync(DoneAsync);
                // assert
                await Task.Delay(100);
                done.Should().BeFalse();
                slowTaskSource.SetResult(true);
                await slowTaskSource.Task;
                await doneTask;
                done.Should().BeTrue();
                await tasks.DrainOutAndDisposeAsync();
            }
        }

        [Fact]
        public async Task Several_tasks_with_different_execution_time_get_an_ordered_execution()
        {
            // arrange
            const int delayMs = 10;
            var completionTimes = new DateTime[10];
            async Task CompletesAsync(int index)
            {
                completionTimes[index] = DateTime.Now;
                await Task.Delay(delayMs);
            }
            // act
            using (var tasks = new TaskQueue())
            {
                _ = tasks.EnqueueAsync(ct => CompletesAsync(0));
                _ = tasks.EnqueueAsync(ct => CompletesAsync(1));
                _ = tasks.EnqueueAsync(ct => CompletesAsync(2));
                _ = tasks.EnqueueAsync(ct => CompletesAsync(3));
                _ = tasks.EnqueueAsync(ct => CompletesAsync(4));
                _ = tasks.EnqueueAsync(ct => CompletesAsync(5));
                _ = tasks.EnqueueAsync(ct => CompletesAsync(6));
                _ = tasks.EnqueueAsync(ct => CompletesAsync(7));
                _ = tasks.EnqueueAsync(ct => CompletesAsync(8));
                _ = tasks.EnqueueAsync(ct => CompletesAsync(9));
                await tasks.DrainOutAndDisposeAsync();
            }
            // assert
            for (var i = 1; i < completionTimes.Length; i++)
            {
                var currentTime = completionTimes[i];
                var previousTime = completionTimes[i-1];
                currentTime.Should().BeAfter(previousTime, $"because execution {i} should have happened after at least 10ms from previous execution {completionTimes[i-1]:ss's'fff'ms'}");
            }
        }
    }
}
