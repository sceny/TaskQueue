using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Sceny.Tests
{
    public class TaskQueueTests
    {
        private readonly ILogger _logger;
        private readonly ITaskQueueStubs _stubs;

        public TaskQueueTests()
        {
            _logger = A.Fake<ILogger>();
            _stubs = A.Fake<ITaskQueueStubs>();
        }

        [Fact]
        public async Task Basic_sync_action_execution()
        {
            // arrangedot
            var done = false;
            void DoSomething() => done = true;
            // act
            using (var tasks = new TaskQueue(_logger))
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
            bool DoSomething() => true;
            bool done;
            // act
            using (var tasks = new TaskQueue(_logger))
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
            using (var tasks = new TaskQueue(_logger))
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
            using (var tasks = new TaskQueue(_logger))
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
            using (var tasks = new TaskQueue(_logger))
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
            using (var tasks = new TaskQueue(_logger))
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
            using (var tasks = new TaskQueue(_logger))
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
            using (var tasks = new TaskQueue(_logger))
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
            using (var tasks = new TaskQueue(_logger))
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
            var randomDelay = new Random();
            var executionIndexQueue = new Queue<int>();
            async Task CompletesAsync(int index)
            {
                executionIndexQueue.Enqueue(index);
                var delayInMilliseconds = randomDelay.Next(5, 15);
                await Task.Delay(delayInMilliseconds);
            }
            int runningAfterDrainingOut;
            // act
            using (var tasks = new TaskQueue(_logger))
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
                runningAfterDrainingOut = tasks.Running;
            }
            // assert
            runningAfterDrainingOut.Should().Be(0);
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
            var stopWatch = new Stopwatch();
            void DoSomething() { stopWatch.Stop(); };
            // act
            TimeSpan elapsedTime;
            using (var tasks = new TaskQueue(_logger))
            {
                stopWatch.Start();
                await tasks.Enqueue(DoSomething, 25);
            }
            elapsedTime = stopWatch.Elapsed;
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
            using (var tasks = new TaskQueue(_logger))
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
            var stopWatch = new Stopwatch();
            Task DoSomethingAsync(CancellationToken token) 
            {
                stopWatch.Stop();
                return Task.CompletedTask;
            }
            // act
            TimeSpan elapsedTime;
            using (var tasks = new TaskQueue(_logger))
            {
                stopWatch.Start();
                await tasks.Enqueue(DoSomethingAsync, 15);
            }
            elapsedTime = stopWatch.Elapsed;
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
            using (var tasks = new TaskQueue(_logger))
            {
                _ = tasks.Enqueue(DoSomethingAsync);
                await Task.Delay(5);
                doing.Should().BeTrue();
                await tasks.DrainOutAsync();
            }
            // assert
            
            doing.Should().BeTrue();
        }

        [Fact]
        public async Task Exception_does_not_prevents_remaining_items_on_queue_to_be_processed()
        {
            // arrange
            A.CallTo(() => _stubs.Func<string>()).Returns("I'm good!");
            A.CallTo(() => _stubs.ActionAsync(A<CancellationToken>._)).Invokes(() => throw new NotImplementedException("This method is supposed to fails."));
            A.CallTo(() => _stubs.FuncAsync<string>(A<CancellationToken>._)).Returns(Task.FromResult("I'm good as well!"));
            // act
            Task actionTask;
            Task<string> funcTask;
            Task actionAsyncTask;
            Task<string> funcAsyncTask;
            using (var tasks = new TaskQueue(_logger))
            {
                actionTask = tasks.Enqueue(_stubs.Action);
                funcTask = tasks.Enqueue(_stubs.Func<string>);
                actionAsyncTask = tasks.Enqueue(_stubs.ActionAsync);
                funcAsyncTask = tasks.Enqueue(_stubs.FuncAsync<string>);
                await tasks.DrainOutAsync();
            }
            // assert
            actionTask.IsCompleted.Should().BeTrue();
            actionTask.IsFaulted.Should().BeFalse();

            funcTask.IsCompleted.Should().BeTrue();
            funcTask.IsFaulted.Should().BeFalse();

            actionAsyncTask.IsCompleted.Should().BeTrue();
            actionAsyncTask.IsFaulted.Should().BeTrue();
            actionAsyncTask.Exception.Should().BeOfType<AggregateException>().Which
                .InnerException.Should().BeOfType<NotImplementedException>().Which
                .Message.Should().Contain("This method is supposed to fails.");
            
            funcAsyncTask.IsCompleted.Should().BeTrue();
            funcAsyncTask.IsFaulted.Should().BeFalse();
            
            A.CallTo(() => _stubs.Action()).MustHaveHappenedOnceExactly()
                .Then(A.CallTo(() => _stubs.Func<string>()).MustHaveHappenedOnceExactly())
                .Then(A.CallTo(() => _stubs.ActionAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly())
                .Then(A.CallTo(() => _stubs.FuncAsync<string>(A<CancellationToken>._)).MustHaveHappenedOnceExactly());
        }

        public interface ITaskQueueStubs
        {
            void Action();
            T Func<T>();
            Task ActionAsync(CancellationToken token);
            Task<T> FuncAsync<T>(CancellationToken token);
        }
    }
}
