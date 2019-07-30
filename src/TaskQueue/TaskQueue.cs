using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Sceny
{
    public class TaskQueue: IDisposable
    {
        private bool _isDisposed = false; // To detect redundant calls
        private bool _isDisposing = false; // To detect redundant calls

        private readonly ConcurrentQueue<TaskRunner> _tasksRunners = new ConcurrentQueue<TaskRunner>();
        private readonly SemaphoreSlim _queueProcessingPending = new SemaphoreSlim(0);
        private readonly CancellationTokenSource _processQueueCancellationSource = new CancellationTokenSource();
        private readonly Task _processQueueContinuouslyTask;

        public TaskQueue(CancellationToken cancellationToken = default)
        {
            _processQueueCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _processQueueContinuouslyTask = ProcessQueueContinuouslyAsync(_processQueueCancellationSource.Token);
        }

        public Task EnqueueAsync(Action action)
        {
            CheckDisposed();
            CheckDisposing();
            if (action is null)
                throw new ArgumentNullException(nameof(action));
            var runningTask = new TaskRunner(action);
            _tasksRunners.Enqueue(runningTask);
            _queueProcessingPending.Release();
            return runningTask.Task;
        }

        public Task EnqueueAsync(Func<CancellationToken, Task> actionAsync)
        {
            CheckDisposed();
            CheckDisposing();
            if (actionAsync is null)
                throw new ArgumentNullException(nameof(actionAsync));

            var runningTask = new TaskRunner(actionAsync);
            _tasksRunners.Enqueue(runningTask);
            _queueProcessingPending.Release();
            return runningTask.Task;
        }

        public async Task DrainOutAndDisposeAsync(CancellationToken cancellationToken = default)
        {
            CheckDisposed();
            CheckDisposing();
            _isDisposing = true;
            _queueProcessingPending.Release(); // Pushing one additional processing to close the ProcessQueueContinuouslyAsync loop on _isDisposing == true
            await _processQueueContinuouslyTask;
            while (!_tasksRunners.IsEmpty)
            {
                await _queueProcessingPending.WaitAsync(cancellationToken);
                if (!_tasksRunners.TryDequeue(out var taskRunner))
                    break;
                await taskRunner.RunActionAsync(cancellationToken);
            }
            Dispose();
        }

        private async Task ProcessQueueContinuouslyAsync(CancellationToken cancellationToken)
        {
            while (!_isDisposed)
            {
                if (_isDisposed || _isDisposing)
                    break;
                await _queueProcessingPending.WaitAsync(cancellationToken);
                if (!_tasksRunners.TryDequeue(out var taskRunner))
                    continue;
                await taskRunner.RunActionAsync(cancellationToken);
            }
        }

        private void CheckDisposing()
        {
            if (_isDisposing)
            throw new ObjectDisposedException(nameof(TaskQueue), "The object is being disposed and no new actions can be enqueued.");
        }

        private void CheckDisposed()
        {
            if (_isDisposed)
            throw new ObjectDisposedException(nameof(TaskQueue), "The object is disposed and it can not be used anymore.");
        }

        #region IDisposable

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed)
                return;
            _isDisposing = true;
            if (disposing)
            {
                _processQueueCancellationSource?.Cancel();
                _processQueueCancellationSource?.Dispose();
                _queueProcessingPending?.Dispose();
            }

            _isDisposed = true;
            _isDisposing = false;
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
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