using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Sceny.Benchmark
{
    class Program
    {
        static async Task Main()
        {
            var serviceCollection = new ServiceCollection();
            var serviceProvider = serviceCollection
                .AddLogging(cfg => cfg.AddConsole())
                .Configure<LoggerFilterOptions>(cfg => cfg.MinLevel=LogLevel.Information)
                .BuildServiceProvider();
            var logger = serviceProvider.GetService<ILogger<Program>>();

            for (var i = 0; i < 10000; i++)
            {
                await EqueueBasicFuncAsync(logger);
                if ((i+1) % 5000 == 0)
                    logger.LogInformation($"{i+1} tasks were executed and so far so good");
            }
        }

        private static async Task EqueueBasicFuncAsync(ILogger<Program> logger)
        {
            // arrange
            bool DoSomething() => true;
            bool done;
            // act
            using (var tasks = new TaskQueue(logger))
            {
                var funcTask = tasks.Enqueue(DoSomething);
                await tasks.DrainOutAsync();
                Debug.Assert(funcTask.IsCompleted);
                done = funcTask.Result;
            }
            // assert
            Debug.Assert(done);
        }
    }
}
