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
            var taskRunner = new TaskRunner(Action);
            await taskRunner.RunActionAsync();
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
            var taskRunner = new TaskRunner(ActionAsync);
            await taskRunner.RunActionAsync();
            // assert
            done.Should().BeTrue();
        }

        [Fact]
        public async Task Exceptions_propagates_when_using_sync()
        {
            // arrange
            void Action() => throw new Exception("Action tested exception");
            // act
            var taskRunner = new TaskRunner(Action);
            Func<Task> runAsync = () => taskRunner.RunActionAsync(default);
            // assert
            await runAsync.Should().ThrowAsync<Exception>().WithMessage("Action tested exception");
        }

        [Fact]
        public async Task Exceptions_propagates_when_using_async()
        {
            // arrange
            Task ActionAsync(CancellationToken cancellationToken) => throw new Exception("ActionAsync tested exception");
            // act
            var taskRunner = new TaskRunner(ActionAsync);
            Func<Task> runAsync = () => taskRunner.RunActionAsync(default);
            // assert
            await runAsync.Should().ThrowAsync<Exception>().WithMessage("ActionAsync tested exception");
        }
    }
}