using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sceny
{
    public class TaskRunner<T>
    {
        private CancellationTokenSource _cancelationSource;
        private readonly TaskCompletionSource<T> _taskCompletionSource;

        private TaskRunner()
        {
            Id = Guid.NewGuid();
            EnqueuedAt = DateTime.Now;
            _taskCompletionSource = new TaskCompletionSource<T>();
        }

        public TaskRunner(Func<CancellationToken, Task> actionAsync) : this() => ActionAsync = actionAsync ?? throw new ArgumentNullException(nameof(actionAsync));

        public TaskRunner(Action action) : this()
        {
            if (action is null)
                throw new ArgumentNullException(nameof(action));

            Task actionWrapperAsync(CancellationToken c)
            {
                action();
                return Task.CompletedTask;
            }
            ActionAsync = actionWrapperAsync;
        }

        public Guid Id { get; }
        public DateTimeOffset EnqueuedAt { get; }
        public Task Task => _taskCompletionSource.Task;
        private Func<CancellationToken, Task<T>> ActionAsync { get; }

        public async Task<T> RunActionAsync(CancellationToken cancellationToken = default)
        {
            _cancelationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _ = StartActionAsync(); // rely on _taskCompletionSource to detect its completion
            return await _taskCompletionSource.Task;
        }

        private async Task StartActionAsync()
        {
            try
            {
                var result = await ActionAsync(_cancelationSource.Token);
                if (_cancelationSource.IsCancellationRequested)
                    _taskCompletionSource.SetCanceled();
                else
                    _taskCompletionSource.SetResult(result);
            }
            catch (Exception exception)
            {
                _taskCompletionSource.SetException(exception);
            }
        }

        public override string ToString() => $"{nameof(Id)}: {Id}, {nameof(EnqueuedAt)}: {EnqueuedAt}";
    }
}