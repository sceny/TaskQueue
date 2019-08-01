using System.Threading.Tasks;
using Quartz;

namespace Sceny.Internal
{
    [DisallowConcurrentExecution]
    internal class TaskJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            var taskRunner = context.MergedJobDataMap.GetTaskRunner();
            await taskRunner.RunAsync(context.CancellationToken);
        }
    }
}