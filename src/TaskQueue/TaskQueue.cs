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

        private readonly ConcurrentQueue<TaskRunner> _tasksRunners = new ConcurrentQueue<TaskRunner>();
        private readonly SemaphoreSlim _queueProcessingPending = new SemaphoreSlim(0);
        private readonly CancellationTokenSource _processQueueCancellationSource = new CancellationTokenSource();
        private readonly Task _processQueueContinuouslyTask;

        public TaskQueue(CancellationToken cancellationToken = default)
        {
            _processQueueCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _processQueueContinuouslyTask = ProcessQueueContinuouslyAsync(_processQueueCancellationSource.Token);
        }

        public Task EnqueueAsync(Action action, int delayInMilliseconds = 0)
        {
            CheckDisposed();
            CheckDrainingOut();
            CheckDelayInMilliseconds(delayInMilliseconds);
            if (action is null)
                throw new ArgumentNullException(nameof(action));
            if (delayInMilliseconds > 0)
                EnqueueTaskRunnerDelayAsync(delayInMilliseconds);
            var taskRunner = new TaskRunner(action);
            return EnqueueTaskRunnerAsync(taskRunner);
        }

        public Task EnqueueAsync(Func<CancellationToken, Task> actionAsync, int delayInMilliseconds = 0)
        {
            CheckDisposed();
            CheckDrainingOut();
            CheckDelayInMilliseconds(delayInMilliseconds);
            if (actionAsync is null)
                throw new ArgumentNullException(nameof(actionAsync));
            if (delayInMilliseconds > 0)
                EnqueueTaskRunnerDelayAsync(delayInMilliseconds);
            var runningTask = new TaskRunner(actionAsync);
            return EnqueueTaskRunnerAsync(runningTask);
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
                await taskRunner.RunActionAsync(cancellationToken);
            }
        }

        private async Task ProcessQueueContinuouslyAsync(CancellationToken cancellationToken)
        {
            while (!_isDisposed && !_isDrainingOut)
            {
                await _queueProcessingPending.WaitAsync(cancellationToken);
                if (!_tasksRunners.TryDequeue(out var taskRunner))
                    continue;
                await taskRunner.RunActionAsync(cancellationToken);
            }
        }

        private Task EnqueueTaskRunnerDelayAsync(int delayInMilliseconds)
        {
            Task DelayAsync(CancellationToken cancelationToken) => Task.Delay(delayInMilliseconds);
            var taskRunner = new TaskRunner(DelayAsync);
            return EnqueueTaskRunnerAsync(taskRunner);
        } 

        private Task EnqueueTaskRunnerAsync(TaskRunner taskRunner)
        {
            _tasksRunners.Enqueue(taskRunner);
            _queueProcessingPending.Release();
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