using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sceny
{
    public abstract class TaskRunnerBase
    {
        protected TaskRunnerBase()
        {
            Id = Guid.NewGuid();
            EnqueuedAt = DateTime.Now;
        }
        public TaskRunnerBase(Action action) : this() => Action = action ?? throw new ArgumentNullException(nameof(action));
        public TaskRunnerBase(Func<CancellationToken, Task> actionAsync) : this() => ActionAsync = actionAsync ?? throw new ArgumentNullException(nameof(actionAsync));
        protected Action Action { get; }
        protected Func<CancellationToken, Task> ActionAsync { get; }

        public Guid Id { get; }
        public DateTimeOffset EnqueuedAt { get; }
        public abstract Task RunAsync(CancellationToken cancellationToken = default);
    }

    public class TaskRunner<T> : TaskRunnerBase
    {
        private readonly TaskCompletionSource<T> _taskCompletionSource = new TaskCompletionSource<T>();
        public TaskRunner(Action action) : base(action) { }
        public TaskRunner(Func<T> func) : base() => Func = func ?? throw new ArgumentNullException(nameof(func));
        public TaskRunner(Func<CancellationToken, Task> actionAsync) : base(actionAsync) { }
        public TaskRunner(Func<CancellationToken, Task<T>> funcAsync) : base() => FuncAsync = funcAsync ?? throw new ArgumentNullException(nameof(funcAsync));
        private Func<T> Func { get; }
        private Func<CancellationToken, Task<T>> FuncAsync { get; }

        public Task<T> FunctionTask => _taskCompletionSource.Task;

        public async Task<T> RunFunctionAsync(CancellationToken cancellationToken = default)
        {
            await RunAsync(cancellationToken);
            return FunctionTask.Result;
        }

        public async override Task RunAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                T result = default;

                Action?.Invoke();
                if (ActionAsync != null) await ActionAsync?.Invoke(cancellationToken);

                if (Func != null) result = Func();
                if (FuncAsync != null) result = await FuncAsync(cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                    _taskCompletionSource.SetCanceled();
                else
                    _taskCompletionSource.SetResult(result);
            }
            catch (Exception exception)
            {
                _taskCompletionSource.SetException(exception);
            }
            await FunctionTask;
        }

        public override string ToString() => $"{nameof(Id)}: {Id}, {nameof(EnqueuedAt)}: {EnqueuedAt}";
    }
}