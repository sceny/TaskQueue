using System;
using Quartz;

namespace Sceny.Internal
{
    internal class QuartzFactory
    {
        public const string TaskRunnerKey = "TaskRunner";
        public QuartzFactory(TaskQueue taskQueue) => TaskQueue = taskQueue ?? throw new System.ArgumentNullException(nameof(taskQueue));
        public string QuartzGroup => $"{nameof(Sceny)}.{nameof(TaskQueue)}";
        public string QuartzPrefix => $"{nameof(TaskQueue)}-{TaskQueue.Id}";

        public TaskQueue TaskQueue { get; }

        public IJobDetail CreateJob() => JobBuilder.Create<TaskJob>()
            .StoreDurably(true)
            .WithIdentity($"{QuartzPrefix}-job", QuartzGroup)
            .Build();

        public ITrigger CreateTrigger<T>(IJobDetail job, TaskRunner<T> taskRunner) => UpdateJobDataMap(
            job,
            taskRunner,
            TriggerBuilder.Create().StartNow()
        );

        public ITrigger CreateTrigger<T>(IJobDetail job, TaskRunner<T> taskRunner, int startDelayInMilliseconds) => UpdateJobDataMap(
            job,
            taskRunner,
            TriggerBuilder.Create()
                .StartAt(DateBuilder.FutureDate(startDelayInMilliseconds, IntervalUnit.Millisecond))
        );

        private ITrigger UpdateJobDataMap<T>(IJobDetail job, TaskRunner<T> taskRunner, TriggerBuilder builder)
        {
            if (job is null)
                throw new ArgumentNullException(nameof(job));
            if (taskRunner is null)
                throw new ArgumentNullException(nameof(taskRunner));

            var trigger = builder
                .WithIdentity($"{QuartzPrefix}-t{Guid.NewGuid()}", QuartzGroup)
                .ForJob(job.Key)
                .Build();
            trigger.JobDataMap.Add(TaskRunnerKey, taskRunner);
            return trigger;
        }
    }
}