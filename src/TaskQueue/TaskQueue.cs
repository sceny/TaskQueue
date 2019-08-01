using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Quartz;
using Quartz.Impl;
using Sceny.Internal;

namespace Sceny
{
    public class TaskQueue : IDisposable
    {
        private bool _isDisposed = false;
        private bool _isDrainingOut = false;

        private readonly CancellationTokenSource _processQueueCancellationSource = new CancellationTokenSource();
        private readonly IJobDetail _job;
        private readonly QuartzFactory _quartzFactory;
        private readonly AsyncLazy<IScheduler> _schedulerAsync;
        private IScheduler _scheduler;
        private int _activeJobsCount = 0;

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
            //    var props = new NameValueCollection
            //    {
            //     //    { "quartz.serializer.type", "binary" },
            //     //    { "quartz.jobStore.type", "Quartz.Simpl.RAMJobStore" },
            //        { "quartz.threadPool.threadCount", "1" }
            //    };
               var factory = new StdSchedulerFactory();
               var scheduler = await factory.GetScheduler(ct);
               await scheduler.Start(ct);
               _scheduler = scheduler;
                await scheduler.AddJob(_job, true, ct);
               return scheduler;
           }, cancellationToken);
        }

        public Guid Id { get; } = Guid.NewGuid();
        public int ActiveTaskCount { get => _activeJobsCount; }
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
            var taskRunner = new TaskRunner<bool>(action);
            return await EnqueueRunnerAsync(taskRunner, delayInMilliseconds, cancellationToken);
        }

        public async Task<Task<T>> EnqueueAsync<T>(
            Func<T> func,
            int delayInMilliseconds = 0,
            CancellationToken cancellationToken = default
        )
        {
            CheckDisposed();
            CheckDrainingOut();
            if (func is null) throw new ArgumentNullException(nameof(func));

            var taskRunner = new TaskRunner<T>(func);
            return await EnqueueRunnerAsync(taskRunner, delayInMilliseconds, cancellationToken);
        }

        public async Task<Task> EnqueueAsync(
            Func<CancellationToken, Task> actionAsync,
            int delayInMilliseconds = 0,
            CancellationToken cancellationToken = default
        )
        {
            CheckDisposed();
            CheckDrainingOut();
            if (actionAsync is null) throw new ArgumentNullException(nameof(actionAsync));

            var taskRunner = new TaskRunner<bool>(actionAsync);
            return await EnqueueRunnerAsync(taskRunner, delayInMilliseconds, cancellationToken);
        }

        public async Task<Task<T>> EnqueueAsync<T>(
            Func<CancellationToken, Task<T>> funcAsync,
            int delayInMilliseconds = 0,
            CancellationToken cancellationToken = default
        )
        {
            CheckDisposed();
            CheckDrainingOut();
            if (funcAsync is null) throw new ArgumentNullException(nameof(funcAsync));

            var taskRunner = new TaskRunner<T>(funcAsync);
            return await EnqueueRunnerAsync(taskRunner, delayInMilliseconds, cancellationToken);
        }

        public async Task DrainOutAsync(TimeSpan timeout = default, CancellationToken cancellationToken = default)
        {
            CheckDisposed();
            CheckDrainingOut();

            _isDrainingOut = true;
            if (timeout == default)
                timeout = TimeSpan.FromSeconds(30);

            if (!_schedulerAsync.IsValueCreated)
                return;
            var scheduler = await GetSchedulerAsync();

            await scheduler.Standby();
            var stopwatch = Stopwatch.StartNew();
            int activeJobsCount; // Use this to use the same value beetween the evaluation and the timeout error message, to prevent odd messages like timeout because there are 0 active messages.
            while ((activeJobsCount = _activeJobsCount) > 0)
            {
                if (stopwatch.Elapsed > timeout)
                    throw new TimeoutException($"There are still {activeJobsCount} active jobs but the draining out operation has been timed out.");
                await Task.Delay(100);
            }
            await scheduler.Shutdown(true, cancellationToken);
        }

        private async Task<Task<T>> EnqueueRunnerAsync<T>(
            TaskRunner<T> runner,
            int delayInMilliseconds,
            CancellationToken cancellationToken = default
        )
        {
            if (runner is null)
                throw new ArgumentNullException(nameof(runner));
            CheckDelayInMilliseconds(delayInMilliseconds);

            var trigger = delayInMilliseconds > 0
                ? _quartzFactory.CreateTrigger(
                      _job,
                      runner,
                      delayInMilliseconds
                  )
                : _quartzFactory.CreateTrigger(
                      _job,
                      runner
                  );

            var scheduler = await GetSchedulerAsync();
            Interlocked.Increment(ref _activeJobsCount);
            await scheduler.ScheduleJob(trigger, cancellationToken);

            _ = runner.FunctionTask.ContinueWith(t => Interlocked.Decrement(ref _activeJobsCount));
            return runner.FunctionTask;
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