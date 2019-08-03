using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Sceny
{
    public class TaskQueue: IDisposable
    {
        private bool _isDisposed = false; // To detect redundant calls
        private bool _isDrainingOut = false; // To detect redundant calls

        private readonly ConcurrentQueue<TaskRunnerBase> _tasksRunners = new ConcurrentQueue<TaskRunnerBase>();
        private readonly SemaphoreSlim _queueProcessingPending = new SemaphoreSlim(0);
        private readonly CancellationTokenSource _processQueueCancellationSource = new CancellationTokenSource();
        private readonly Task _processQueueContinuouslyTask;

        public TaskQueue(CancellationToken cancellationToken = default)
        {
            _processQueueCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _processQueueContinuouslyTask = Task.Factory.StartNew(async () => await ProcessQueueContinuouslyAsync(_processQueueCancellationSource.Token), TaskCreationOptions.LongRunning);
        }

        public Task Enqueue(
            Action action,
            int delayInMilliseconds = 0
        )
        {
            CheckDisposed();
            CheckDrainingOut();
            if (action is null) throw new ArgumentNullException(nameof(action));
            var taskRunner = new TaskRunner<bool>(action);
            return Enqueue(taskRunner, delayInMilliseconds);
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
            return Enqueue(taskRunner, delayInMilliseconds);
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
            return Enqueue(taskRunner, delayInMilliseconds);
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
            return Enqueue(taskRunner, delayInMilliseconds);
        }

        public async Task DrainOutAsync(CancellationToken cancellationToken = default)
        {
            CheckDisposed();
            CheckDrainingOut();
            _isDrainingOut = true;
            _queueProcessingPending.Release(); // Pushing one additional processing to close the ProcessQueueContinuouslyAsync loop on _isDisposing == true
            await _processQueueContinuouslyTask;
            while (!_tasksRunners.IsEmpty)
            {
                await _queueProcessingPending.WaitAsync(cancellationToken);
                if (!_tasksRunners.TryDequeue(out var taskRunner))
                    break;
                await taskRunner.RunAsync(cancellationToken);
            }
        }

        private async Task ProcessQueueContinuouslyAsync(CancellationToken cancellationToken)
        {
            while (!_isDisposed && !_isDrainingOut)
            {
                await _queueProcessingPending.WaitAsync(cancellationToken);
                if (!_tasksRunners.TryDequeue(out var taskRunner))
                    continue;
                await taskRunner.RunAsync(cancellationToken);
            }
        }

        private Task<T> Enqueue<T>(
            TaskRunner<T> runner,
            int delayInMilliseconds
        )
        {
            if (runner is null)
                throw new ArgumentNullException(nameof(runner));
            CheckDelayInMilliseconds(delayInMilliseconds);

            if (delayInMilliseconds > 0)
            {
                var delayRunner = new TaskRunner<bool>(DelayAsync);
                _tasksRunners.Enqueue(delayRunner);
                _queueProcessingPending.Release();
            }

            _tasksRunners.Enqueue(runner);
            _queueProcessingPending.Release();
            return runner.FunctionTask;

            Task DelayAsync(CancellationToken cancelationToken) => Task.Delay(delayInMilliseconds);
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
                _queueProcessingPending?.Dispose();
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