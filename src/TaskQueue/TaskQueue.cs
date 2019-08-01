using System;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;
using Quartz;
using Quartz.Impl;
using Sceny.Internal;

namespace Sceny
{
    public class TaskQueue : IDisposable
    {

        private bool _isDisposed = false; // To detect redundant calls
        private bool _isDrainingOut = false; // To detect redundant calls

        private readonly CancellationTokenSource _processQueueCancellationSource = new CancellationTokenSource();
        private readonly IJobDetail _job;
        private readonly QuartzFactory _quartzFactory;
        private readonly AsyncLazy<IScheduler> _schedulerAsync;
        private IScheduler _scheduler;

        private TaskQueue()
        {
            _quartzFactory = new QuartzFactory(this);
            _job = _quartzFactory.CreateJob();
        }

        public TaskQueue(IScheduler scheduler) : this() => _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));

        public TaskQueue(CancellationToken cancellationToken = default) : this()
        {
            _schedulerAsync = new AsyncLazy<IScheduler>(async ct =>
           {
               var props = new NameValueCollection
               {
                //    { "quartz.serializer.type", "binary" },
                //    { "quartz.jobStore.type", "Quartz.Simpl.RAMJobStore" },
                   { "quartz.threadPool.threadCount", "1" }
               };
               var factory = new StdSchedulerFactory(props);
               var scheduler = await factory.GetScheduler(ct);
               await scheduler.Start(ct);
               _scheduler = scheduler;
                await scheduler.AddJob(_job, true, ct);
               return scheduler;
           }, cancellationToken);
        }

        public Guid Id { get; } = Guid.NewGuid();
        private async Task<IScheduler> GetSchedulerAsync() => _scheduler ?? (_scheduler = await _schedulerAsync.Value);
        public async Task<Task> EnqueueAsync(
            Action action,
            int delayInMilliseconds = 0,
            CancellationToken cancellationToken = default
        )
        {
            CheckDisposed();
            CheckDrainingOut();
            if (action is null)
                throw new ArgumentNullException(nameof(action));
            var taskRunner = new TaskRunner(action);
            return await EnqueueTaskRunnerAsync(taskRunner, delayInMilliseconds, cancellationToken);
        }

        public async Task<Task> EnqueueAsync(
            Func<CancellationToken, Task> actionAsync,
            int delayInMilliseconds = 0,
            CancellationToken cancellationToken = default
        )
        {
            CheckDisposed();
            CheckDrainingOut();
            if (actionAsync is null)
                throw new ArgumentNullException(nameof(actionAsync));
            var taskRunner = new TaskRunner(actionAsync);
            return await EnqueueTaskRunnerAsync(taskRunner, delayInMilliseconds, cancellationToken);
        }

        public async Task DrainOutAsync(CancellationToken cancellationToken = default)
        {
            CheckDisposed();
            CheckDrainingOut();
            _isDrainingOut = true;
            if (!_schedulerAsync.IsValueCreated)
                return;
            var scheduler = await GetSchedulerAsync();
            await scheduler.Shutdown(true, cancellationToken);
        }

        private async Task<Task> EnqueueTaskRunnerAsync(
            TaskRunner taskRunner,
            int delayInMilliseconds,
            CancellationToken cancellationToken = default
        )
        {
            if (taskRunner is null)
                throw new ArgumentNullException(nameof(taskRunner));
            CheckDelayInMilliseconds(delayInMilliseconds);

            var trigger = delayInMilliseconds > 0
                ? _quartzFactory.CreateTrigger(
                      _job,
                      taskRunner,
                      delayInMilliseconds
                  )
                : _quartzFactory.CreateTrigger(
                      _job,
                      taskRunner
                  );

            var scheduler = await GetSchedulerAsync();
            await scheduler.ScheduleJob(trigger, cancellationToken);
            return taskRunner.Task;
        }

        private void CheckDrainingOut()
        {
            if (_isDrainingOut)
                throw new InvalidOperationException("The queue is being drained out and no new actions can be enqueued.");
        }

        private void CheckDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(TaskQueue), "The object is disposed and it can not be used anymore.");
        }

        private void CheckDelayInMilliseconds(int delayInMilliseconds)
        {
            if (delayInMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(delayInMilliseconds), "The delay in milliseconds can not be negative. It should be 0 to disable it, or greater than 0.");
        }

        #region IDisposable

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed)
                return;
            _isDrainingOut = true;
            if (disposing)
            {
                _processQueueCancellationSource?.Cancel();
                _processQueueCancellationSource?.Dispose();
                var scheduler = _scheduler;
                if (scheduler != null && !scheduler.IsShutdown)
                    scheduler.Shutdown(false);
            }

            _isDisposed = true;
        }

        public void Dispose() => Dispose(true);

        #endregion
    }
}