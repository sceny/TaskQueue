using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sceny
{
    public class TaskRunner
    {
        private CancellationTokenSource _cancelationSource;
        private readonly TaskCompletionSource<bool> _taskCompletionSource;

        private TaskRunner()
        {
            Id = Guid.NewGuid();
            _taskCompletionSource = new TaskCompletionSource<bool>();
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
        public Task Task => _taskCompletionSource.Task;
        private Func<CancellationToken, Task> ActionAsync { get; }

        public async Task RunActionAsync(CancellationToken cancellationToken = default)
        {
            _cancelationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try
            {
                await ActionAsync(_cancelationSource.Token);
                _taskCompletionSource.SetResult(true);
            }
            catch (Exception exception)
            {
                _taskCompletionSource.SetException(exception);
                throw;
            }
            await _taskCompletionSource.Task;
        }

        public override string ToString() => $"Id: {Id}";
    }
}