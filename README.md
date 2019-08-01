# TaskQueue

[![NuGet Badge](https://buildstats.info/nuget/Sceny.TaskQueue)](https://www.nuget.org/packages/Sceny.TaskQueue/) [![Build Status](https://dev.azure.com/sceny/TaskQueue/_apis/build/status/TaskQueue%20build?branchName=master)](https://dev.azure.com/sceny/TaskQueue/_build/latest?definitionId=1&branchName=master) 

The single goal is to have a no-brainer lightweight way to simple enqueue tasks and consume it in a ordered way. If you do need to have to queues, then just instanciate two queue tasks. 

``` csharp
using (var tasks = new TaskQueue())
{
    _ = tasks.EnqueueAsync(() => Console.WriteLine("Do something"));
    _ = tasks.EnqueueAsync(() => Console.WriteLine("Do another thing only after the previous ones completion."));
    await tasks.DrainOutAsync(); // Wait for everything to completes
}
```

``` csharp
using (var tasks = new TaskQueue())
{
    _ = tasks.EnqueueAsync(() => Console.WriteLine("Do something"));
    _ = tasks.EnqueueAsync(() => Console.WriteLine("Do another thing only after the previous ones completion."));
} // --> Do not calling DrainOutAsync discards all the not ran tasks on disposal.
```

``` csharp
using (var tasks = new TaskQueue())
{
    _ = tasks.EnqueueAsync(() => Console.WriteLine("Do something"));
    _ = tasks.EnqueueAsync(() => Console.WriteLine("Do another thing only after the previous ones completion."));
    await tasks.EnqueueAsync(() => Console.WriteLine("I do care about this task and I will await!"));
    _ = tasks.EnqueueAsync(() => Console.WriteLine("Do at the end!"));
    await tasks.DrainOutAsync(); // Wait for everything to completes
}
```

In the example below, because I am waiting for the steps 3 and 5, the step from 1 to 5 will be executed.

``` csharp
using (var tasks = new TaskQueue())
{
    _ = tasks.EnqueueAsync(() => Console.WriteLine("1. Do something"));
    _ = tasks.EnqueueAsync(() => Console.WriteLine("2. Do another thing only after the previous ones completion."));
    var iDoCareAboutThis = tasks.EnqueueAsync(() => Console.WriteLine("3. I do care about this task!"));
    _ = tasks.EnqueueAsync(() => Console.WriteLine("4. Enqueue one more thing to do."));
    var butIDoCareAboutThisAsWell = tasks.EnqueueAsync(() => Console.WriteLine("5. But, I do care about this as well!"));
    _ = tasks.EnqueueAsync(() => Console.WriteLine("6. Do at the end and it can be discarded. I do not care."));
    await Tasks.WhenAll(iDoCareAboutThis, butIDoCareAboutThisAsWell);
} // --> Do not calling tasks.DrainOutAsync discards all the not ran tasks on disposal.
```

``` csharp
using (var tasks = new TaskQueue())
{
    _ = tasks.EnqueueAsync(() => Console.WriteLine("1. Do something"));
    _ = tasks.EnqueueAsync(() => Console.WriteLine("2. Please proceed to the others, but I do want to do something right after this, but out of the queue."))
        .ContinueWith(t => Console.WriteLine("Do whatever with the task"));
    await tasks.DrainOutAsync(); // Wait for everything to completes
}
```

