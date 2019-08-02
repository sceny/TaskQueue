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
        public async Task Basic_sync_action_execution()
        {
            // arrange
            var done = false;
            void DoSomething() => done = true;
            // act
            using (var tasks = new TaskQueue())
            {
                _ = tasks.Enqueue(DoSomething);
                await tasks.DrainOutAsync();
            }
            // assert
            done.Should().BeTrue();
        }
    
        [Fact]
        public async Task Basic_sync_func_execution()
        {
            // arrange
            var done = false;
            bool DoSomething() => true;
            // act
            using (var tasks = new TaskQueue())
            {
                var funcTask = tasks.Enqueue(DoSomething);
                await tasks.DrainOutAsync();
                funcTask.IsCompleted.Should().BeTrue();
                done = funcTask.Result;
            }
            // assert
            done.Should().BeTrue();
        }

        [Fact]
        public async Task Basic_sync_action_execution_can_be_waited()
        {
            // arrange
            void DoSomething() { };
            // act
            Task somethingTask;
            using (var tasks = new TaskQueue())
            {
                somethingTask = tasks.Enqueue(DoSomething);
                await somethingTask;
                await tasks.DrainOutAsync();
            }
            // assert
            somethingTask.IsCompleted.Should().BeTrue();
        }

        [Fact]
        public async Task Basic_sync_func_execution_can_be_waited()
        {
            // arrange
            bool DoSomething() => true;
            // act
            Task<bool> somethingTask;
            using (var tasks = new TaskQueue())
            {
                somethingTask = tasks.Enqueue(DoSomething);
                await somethingTask;
                await tasks.DrainOutAsync();
            }
            // assert
            somethingTask.IsCompleted.Should().BeTrue();
            somethingTask.Result.Should().BeTrue();
        }

        [Fact]
        public async Task Basic_async_action_execution()
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
                _ = tasks.Enqueue(DoSomethingAsync);
                await tasks.DrainOutAsync();
            }
            // assert
            done.Should().BeTrue();
        }

        [Fact]
        public async Task Basic_async_func_execution()
        {
            // arrange
            var done = false;
            Task<bool> DoSomethingAsync(CancellationToken token) 
            {
                done = true;
                return Task.FromResult(done);
            }
            // act
            using (var tasks = new TaskQueue())
            {
                _ = tasks.Enqueue(DoSomethingAsync);
                await tasks.DrainOutAsync();
            }
            // assert
            done.Should().BeTrue();
        }

        [Fact]
        public async Task Basic_async_action_execution_can_be_waited()
        {
            // arrange
            Task DoSomethingAsync(CancellationToken token) => Task.CompletedTask;
            // act
            Task somethingTask;
            using (var tasks = new TaskQueue())
            {
                somethingTask = tasks.Enqueue(DoSomethingAsync);
                await somethingTask;
                await tasks.DrainOutAsync();
            }
            // assert
            somethingTask.IsCompleted.Should().BeTrue();
        }

        [Fact]
        public async Task Basic_async_func_execution_can_be_waited()
        {
            // arrange
            Task<bool> DoSomethingAsync(CancellationToken token) => Task.FromResult(true);
            // act
            Task<bool> somethingTask;
            using (var tasks = new TaskQueue())
            {
                somethingTask = tasks.Enqueue(DoSomethingAsync);
                await somethingTask;
                await tasks.DrainOutAsync();
            }
            // assert
            somethingTask.IsCompleted.Should().BeTrue();
            somethingTask.Result.Should().BeTrue();
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
                _ = tasks.Enqueue(SlowAsync);
                var doneTask = tasks.Enqueue(DoneAsync);
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
                _ = tasks.Enqueue(ct => CompletesAsync(0));
                _ = tasks.Enqueue(ct => CompletesAsync(1));
                _ = tasks.Enqueue(ct => CompletesAsync(2));
                _ = tasks.Enqueue(ct => CompletesAsync(3));
                _ = tasks.Enqueue(ct => CompletesAsync(4));
                _ = tasks.Enqueue(ct => CompletesAsync(5));
                _ = tasks.Enqueue(ct => CompletesAsync(6));
                _ = tasks.Enqueue(ct => CompletesAsync(7));
                _ = tasks.Enqueue(ct => CompletesAsync(8));
                _ = tasks.Enqueue(ct => CompletesAsync(9));
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
                await tasks.Enqueue(DoSomething, 25);
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
                _ = tasks.Enqueue(DoSomething);
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
                await tasks.Enqueue(DoSomethingAsync, 15);
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
                _ = tasks.Enqueue(DoSomethingAsync);
                await Task.Delay(5);
                doing.Should().BeTrue();
                await tasks.DrainOutAsync();
            }
            // assert
            doing.Should().BeTrue();
        }
    }
}
