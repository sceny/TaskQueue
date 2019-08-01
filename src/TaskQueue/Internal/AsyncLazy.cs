using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Sceny.Internal
{
    public class AsyncLazy<T> : Lazy<Task<T>>
    {
        public AsyncLazy(Func<CancellationToken, Task<T>> taskFactory, CancellationToken cancellationToken = default)
            : base(() => taskFactory(cancellationToken)) { }

        public TaskAwaiter<T> GetAwaiter() => Value.GetAwaiter();
    }
}