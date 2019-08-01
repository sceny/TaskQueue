using System;
using System.Collections.Generic;
using System.Diagnostics;
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
                await tasks.EnqueueAsync(DoSomething);
                await tasks.DrainOutAsync();
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
                somethingTask = await tasks.EnqueueAsync(DoSomething);
                await somethingTask;
                await tasks.DrainOutAsync();
            }
            // assert
            somethingTask.IsCompleted.Should().BeTrue();
        }

        [Fact]
        public async Task Basic_async_task_execution_drained_out_waits_for_its_completion()
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
                await tasks.EnqueueAsync(DoSomethingAsync);
                await tasks.DrainOutAsync();
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
                somethingTask = await tasks.EnqueueAsync(DoSomethingAsync);
                await somethingTask;
                await tasks.DrainOutAsync();
            }
            // assert
            somethingTask.IsCompleted.Should().BeTrue();
        }

        [Fact]
        public async Task Do_not_run_task_before_previous_completion()
        {
            // arrange
            var slowHasBeenCompleted = false;
            var done = false;

            var slowTaskSource = new TaskCompletionSource<bool>();
            Task SlowAsync(CancellationToken token)
            {
                slowHasBeenCompleted = true;
                return slowTaskSource.Task;
            }

            Task DoneAsync(CancellationToken token)
            {
                slowHasBeenCompleted.Should().BeTrue();
                done = true;
                return Task.CompletedTask;
            }
            // act
            using (var tasks = new TaskQueue())
            {
                await tasks.EnqueueAsync(SlowAsync);
                var doneTask = await tasks.EnqueueAsync(DoneAsync);
                // assert
                await Task.Delay(100);
                done.Should().BeFalse();
                slowTaskSource.SetResult(true);
                await slowTaskSource.Task;
                await doneTask;
                done.Should().BeTrue();
                await tasks.DrainOutAsync();
            }
        }

        [Fact]
        public async Task Several_tasks_with_different_execution_time_get_an_ordered_execution()
        {
            // arrange
            const int delayMs = 10;
            var executionIndexQueue = new Queue<int>();
            async Task CompletesAsync(int index)
            {
                executionIndexQueue.Enqueue(index);
                await Task.Delay(delayMs);
            }
            // act
            using (var tasks = new TaskQueue())
            {
                await tasks.EnqueueAsync(ct => CompletesAsync(0));
                await tasks.EnqueueAsync(ct => CompletesAsync(1));
                await tasks.EnqueueAsync(ct => CompletesAsync(2));
                await tasks.EnqueueAsync(ct => CompletesAsync(3));
                await tasks.EnqueueAsync(ct => CompletesAsync(4));
                await tasks.EnqueueAsync(ct => CompletesAsync(5));
                await tasks.EnqueueAsync(ct => CompletesAsync(6));
                await tasks.EnqueueAsync(ct => CompletesAsync(7));
                await tasks.EnqueueAsync(ct => CompletesAsync(8));
                await tasks.EnqueueAsync(ct => CompletesAsync(9));
                await tasks.DrainOutAsync();
            }
            // assert
            for (var expectedIndex = 0; expectedIndex < 10; expectedIndex++)
            {
                var index = executionIndexQueue.Dequeue();
                index.Should().Be(expectedIndex);
            }
        }

        [Fact]
        public async Task Wait_parametized_delay_before_doing_something()
        {
            // arrange
            void DoSomething() { };
            // act
            TimeSpan elapsedTime;
            using (var tasks = new TaskQueue())
            {
                var stopWatch = new Stopwatch();
                stopWatch.Start();
                await tasks.EnqueueAsync(DoSomething, 25);
                stopWatch.Stop();
                elapsedTime = stopWatch.Elapsed;
            }
            // assert
            elapsedTime.TotalMilliseconds.Should().BeGreaterOrEqualTo(25);
        }

        [Fact]
        public async Task Do_not_wait_before_doing_something_if_there_is_no_delay_configured()
        {
            // arrange
            var doing = false;
            void DoSomething() => doing = true;
            // act
            using (var tasks = new TaskQueue())
            {
                await tasks.EnqueueAsync(DoSomething);
                await Task.Delay(5);
                doing.Should().BeTrue();
                await tasks.DrainOutAsync();
            }
            // assert
            doing.Should().BeTrue();
        }

        [Fact]
        public async Task Wait_parametized_delay_before_doing_something_async()
        {
            // arrange
            Task DoSomethingAsync(CancellationToken token) => Task.CompletedTask;
            // act
            TimeSpan elapsedTime;
            using (var tasks = new TaskQueue())
            {
                var stopWatch = new Stopwatch();
                stopWatch.Start();
                await tasks.EnqueueAsync(DoSomethingAsync, 15);
                stopWatch.Stop();
                elapsedTime = stopWatch.Elapsed;
            }
            // assert
            elapsedTime.TotalMilliseconds.Should().BeGreaterOrEqualTo(15);
        }

        [Fact]
        public async Task Do_not_wait_before_doing_something_async_if_there_is_no_delay_configured()
        {
            // arrange
            var doing = false;
            Task DoSomethingAsync(CancellationToken token)
            {
                doing = true;
                return Task.CompletedTask;
            }
            // act
            using (var tasks = new TaskQueue())
            {
                await tasks.EnqueueAsync(DoSomethingAsync);
                await Task.Delay(5);
                doing.Should().BeTrue();
                await tasks.DrainOutAsync();
            }
            // assert
            doing.Should().BeTrue();
        }
    }
}
