using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Sceny.Tests
{
    public class TaskRunnerTests
    {
        [Fact]
        public async Task Run_completes_at_the_actionAsync_execution_completion()
        {
            // arrange
            var done = false;
            void Action() => done = true;
            // act
            var taskRunner = new TaskRunner<bool>(Action);
            await taskRunner.RunAsync();
            // assert
            done.Should().BeTrue();
        }

        [Fact]
        public async Task Run_completes_at_the_actionAsync_execution_completion_with_a_return_value()
        {
            // arrange
            bool Function() => true;
            // act
            var taskRunner = new TaskRunner<bool>(Function);
            var done = await taskRunner.RunFunctionAsync();
            // assert
            done.Should().BeTrue();
        }

        [Fact]
        public async Task RunAsync_completes_at_the_actionAsync_execution_completion()
        {
            // arrange
            var done = false;
            async Task ActionAsync(CancellationToken cancellationToken)
            {
                done = true;
                await Task.Delay(10);
            }
            // act
            var taskRunner = new TaskRunner<bool>(ActionAsync);
            await taskRunner.RunAsync();
            // assert
            done.Should().BeTrue();
        }

        [Fact]
        public async Task RunAsync_completes_at_the_actionAsync_execution_completion_with_a_return_value()
        {
            // arrange
            async Task<bool> FunctionAsync(CancellationToken cancellationToken)
            {
                await Task.Delay(10);
                return true;
            }
            // act
            var taskRunner = new TaskRunner<bool>(FunctionAsync);
            var done = await taskRunner.RunFunctionAsync();
            // assert
            done.Should().BeTrue();
        }

        [Fact]
        public async Task Exceptions_propagates_when_using_sync()
        {
            // arrange
            void Action() => throw new Exception("Action tested exception");
            // act
            var taskRunner = new TaskRunner<bool>(Action);
            Func<Task> runAsync = () => taskRunner.RunAsync(default);
            // assert
            await runAsync.Should().ThrowAsync<Exception>().WithMessage("Action tested exception");
        }

        [Fact]
        public async Task Exceptions_propagates_when_using_async()
        {
            // arrange
            Task ActionAsync(CancellationToken cancellationToken) => throw new Exception("ActionAsync tested exception");
            // act
            var taskRunner = new TaskRunner<bool>(ActionAsync);
            Func<Task> runAsync = () => taskRunner.RunAsync(default);
            // assert
            await runAsync.Should().ThrowAsync<Exception>().WithMessage("ActionAsync tested exception");
        }
    }
}