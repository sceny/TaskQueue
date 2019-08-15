using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Sceny
{
    public class TaskQueue : IDisposable
    {
        private bool _isDisposed = false;
        private bool _isDrainingOut = false;
        private bool _isRunning;
        private int _running;
        private readonly SemaphoreSlim _trackRunnerSemaphore = new SemaphoreSlim(1);
        private readonly SemaphoreSlim _taskRunningSemaphore = new SemaphoreSlim(1);
        private readonly CancellationTokenSource _processQueueCancellationSource = new CancellationTokenSource();
        private readonly ILogger _logger;
        private Task _latestRunningTask = null;

        public TaskQueue(ILogger logger, CancellationToken cancellationToken = default)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _processQueueCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        public int Running => _running;

        public Task Enqueue(
            Action action,
            int delayInMilliseconds = 0
        )
        {
            CheckDisposed();
            CheckDrainingOut();
            if (action is null) throw new ArgumentNullException(nameof(action));
            var taskRunner = new TaskRunner<bool>(action);
            return EnqueueInternal(taskRunner, delayInMilliseconds);
        }

        public Task<T> Enqueue<T>(
            Func<T> func,
            int delayInMilliseconds = 0
        )
        {
            CheckDisposed();
            CheckDrainingOut();
            if (func is null) throw new ArgumentNullException(nameof(func));

            var taskRunner = new TaskRunner<T>(func);
            return EnqueueInternal(taskRunner, delayInMilliseconds);
        }

        public Task Enqueue(
            Func<CancellationToken, Task> actionAsync,
            int delayInMilliseconds = 0
        )
        {
            CheckDisposed();
            CheckDrainingOut();
            if (actionAsync is null) throw new ArgumentNullException(nameof(actionAsync));

            var taskRunner = new TaskRunner<bool>(actionAsync);
            return EnqueueInternal(taskRunner, delayInMilliseconds);
        }

        public Task<T> Enqueue<T>(
            Func<CancellationToken, Task<T>> funcAsync,
            int delayInMilliseconds = 0
        )
        {
            CheckDisposed();
            CheckDrainingOut();
            if (funcAsync is null) throw new ArgumentNullException(nameof(funcAsync));

            var taskRunner = new TaskRunner<T>(funcAsync);
            return EnqueueInternal(taskRunner, delayInMilliseconds);
        }

        private async Task<T> EnqueueInternal<T>(TaskRunner<T> runner, int delayInMilliseconds)
        {
            if (runner is null)
                throw new ArgumentNullException(nameof(runner));

            if (delayInMilliseconds > 0)
            {
                CheckDelayInMilliseconds(delayInMilliseconds);
                var delayRunner = new TaskRunner<bool>(DelayAsync);
                await EnqueueInternal(delayRunner, 0);
            }

            await _trackRunnerSemaphore.WaitAsync();
            try
            {
                ++_running;
                _latestRunningTask = RunInBackgroundAsync(runner)
                    .ContinueWith(t => Interlocked.Decrement(ref _running));
            }
            finally
            {
                _trackRunnerSemaphore.Release();
            }

            return await runner.FunctionTask;

            Task DelayAsync(CancellationToken cancelationToken) => Task.Delay(delayInMilliseconds);
        }

        private async Task RunInBackgroundAsync<T>(TaskRunner<T> runner)
        {
            if (runner is null)
                throw new ArgumentNullException(nameof(runner));
            using (_logger.BeginScope($"{nameof(TaskQueue)}.{nameof(Enqueue)}"))
            {
                await _taskRunningSemaphore.WaitAsync(_processQueueCancellationSource.Token);
                try
                {
                    ChangeStatusToRunning();
                    try
                    {
                        if (_logger.IsEnabled(LogLevel.Debug))
                            _logger.LogDebug($"Running {runner} while draining out the queue in {nameof(Enqueue)}.");
                        await runner.RunAsync(_processQueueCancellationSource.Token); // Ignore the method token and use the global
                        ChangeStatusToNotRunning();
                    }
                    catch (Exception exception)
                    {
                        var runnerName = runner?.ToString() ?? "null";
                        _logger.LogError($"An unhandled exception happened while processing the task '{runnerName}' while {nameof(Enqueue)}. Exception: {exception}", exception);
                        ChangeStatusToNotRunning(true);
                    }
                }
                finally
                {
                    _taskRunningSemaphore.Release();
                }
            }
        }

        public async Task DrainOutAsync(CancellationToken cancellationToken = default)
        {
            CheckDisposed();
            CheckDrainingOut();
            _isDrainingOut = true;

            await _trackRunnerSemaphore.WaitAsync(cancellationToken);
            try
            {
                var latestRunningTask = _latestRunningTask;
                if (latestRunningTask != null)
                    await latestRunningTask;
            }
            finally
            {
                _trackRunnerSemaphore.Release();
            }
        }

        [Conditional("DEBUG")]
        private void ChangeStatusToNotRunning(bool force = false)
        {
#if DEBUG
            var isRunning = _isRunning;
            if (!isRunning && !force)
                throw new InvalidOperationException("There is no active runner and a stop running flagging operations runner was requested. This is a severe failure and you should consider reseting the queue by creating a new instance.");
            _isRunning = false;
#endif
        }

        [Conditional("DEBUG")]
        private void ChangeStatusToRunning()
        {
#if DEBUG
            var isRunning = _isRunning;
            if (isRunning)
                throw new InvalidOperationException("There is an active runner and a new runner was requested. This is a severe failure and you should consider reseting the queue by creating a new instance.");
            _isRunning = true;
#endif
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
                _trackRunnerSemaphore?.Dispose();
                _taskRunningSemaphore?.Dispose();
            }

            _isDisposed = true;
        }

        ~TaskQueue()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}