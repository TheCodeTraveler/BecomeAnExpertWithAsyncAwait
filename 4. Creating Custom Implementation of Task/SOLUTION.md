# Solution Walkthrough: Creating Custom Implementation of Task

Use this during the guided walkthrough after the challenge and group review in [README.md](README.md).

The completed app is **2. Finish/CoffeeShop**. Compare your implementation with the finished sample as we rebuild the solution step by step, replacing each `NotImplementedException` stub from the starter's `CustomTask.cs` along the way.

## 1. Track Completion State

`CustomTask` needs shared state that can be read and updated safely across threads:

```cs
readonly Lock _lock = new();
readonly List<(Action Continuation, ExecutionContext? Context)> _continuations = [];

bool _completed;
Exception? _exception;
```

A real task can have more than one continuation: every `ContinueWith(...)`, every `Wait()`, and every `await` registers one. That is why the pending continuations are a list of continuation/context pairs instead of a single `Action` field.

Expose completion through a locked property:

```cs
public bool IsCompleted
{
    get
    {
        lock (_lock)
        {
            return _completed;
        }
    }
}
```

## 2. Run Work on the Thread Pool

`Run(Action)` creates a task, queues the action, and completes the task when the action finishes:

```cs
public static CustomTask Run(Action action)
{
    CustomTask task = new();

    ThreadPool.QueueUserWorkItem(_ =>
    {
        try
        {
            action();
            task.SetResult();
        }
        catch (Exception e)
        {
            task.SetException(e);
        }
    });

    return task;
}
```

## 3. Add Continuations

`ContinueWith(Action)` returns a new `CustomTask` that represents the continuation work. That returned task must complete when the continuation succeeds, and it must store the continuation exception when the continuation fails.

The subtle bug is registering into a single `_action` field. Each registration before completion would replace the previous continuation, so two callers awaiting or waiting on the same `CustomTask` would leave the first continuation, and its returned task, permanently incomplete. Add every pending continuation to the list instead:

```cs
public CustomTask ContinueWith(Action action)
{
    CustomTask task = new();

    lock (_lock)
    {
        if (_completed)
        {
            ThreadPool.QueueUserWorkItem(_ => CompleteContinuationTask());
        }
        else
        {
            _continuations.Add((CompleteContinuationTask, ExecutionContext.Capture()));
        }
    }

    return task;

    void CompleteContinuationTask()
    {
        try
        {
            action();
            task.SetResult();
        }
        catch (Exception e)
        {
            task.SetException(e);
        }
    }
}
```

Capturing `ExecutionContext` with each continuation lets every continuation observe its own caller's async-local state, culture, and principal.

## 4. Complete the Task Once

`SetResult()` and `SetException(Exception)` can share the same completion logic:

```cs
public void SetResult() => CompleteTask(null);

public void SetException(Exception exception) => CompleteTask(exception);
```

The shared completion method marks the antecedent task complete, stores any exception, and drains every registered continuation, queuing each one and restoring the `ExecutionContext` that was captured when it was registered:

```cs
void CompleteTask(Exception? exception)
{
    List<(Action Continuation, ExecutionContext? Context)> continuationsToRun;

    lock (_lock)
    {
        if (_completed)
            throw new InvalidOperationException($"{nameof(CustomTask)} already completed. Cannot complete an already completed {nameof(CustomTask)}");

        _completed = true;
        _exception = exception;

        continuationsToRun = [.. _continuations];
        _continuations.Clear();
    }

    // Always queue (never inline) to avoid long ContinueWith chains recursively executing through CompleteTask.
    // UnsafeQueueUserWorkItem skips capturing this thread's context; the registrar's context is restored inside the work item.
    foreach (var pending in continuationsToRun)
    {
        ThreadPool.UnsafeQueueUserWorkItem(static state =>
        {
            var (continuation, context) = state;

            if (context is null)
            {
                continuation.Invoke();
            }
            else
            {
                ExecutionContext.Run(context, static s => ((Action?)s)?.Invoke(), continuation);
            }
        }, pending, preferLocal: false);
    }
}
```

Copy the list and clear it inside the lock, then dispatch outside the lock. Because `_completed` is already `true`, any continuation registered while draining takes the completed branch of `ContinueWith(...)` and is queued directly, so nothing is lost.

There are two subtle decisions in the dispatch loop.

First, every continuation is queued to the thread pool rather than invoked inline. If `CompleteTask(...)` invoked continuations directly, a long `ContinueWith` chain would recurse: the continuation calls `task.SetResult()`, which calls `CompleteTask(...)`, which invokes the next continuation, and so on, one stack frame per link, until the stack overflows. Queuing breaks that recursion, and it also makes scheduling uniform: a continuation is dispatched the same way regardless of whether its registrar had `ExecutionContext` flow suppressed.

Second, the queue call is `ThreadPool.UnsafeQueueUserWorkItem(...)`, not `QueueUserWorkItem(...)`. The `Unsafe` variant does not capture the *completing* thread's `ExecutionContext`. That matters because the completing thread's ambient state has nothing to do with the continuation. The registrar's context is what should apply, and it is restored inside the work item with `ExecutionContext.Run(...)` when one was captured. When `Capture()` returned `null`, the registrar had `SuppressFlow()` active and was explicitly asking not to carry any ambient state, so the continuation runs with the pool thread's default context and nothing leaks in from either side.

> **Why not a concurrent collection?** A `ConcurrentQueue<T>` would make individual adds thread-safe, but the lock protects a bigger invariant: checking `_completed` and registering a continuation must happen atomically. Without the lock, a continuation could be enqueued just after `CompleteTask(...)` drained the queue, and it would never run. Since the lock is required either way, the plain `List<T>` is the simpler, correct container. .NET's real `Task` avoids the lock with `Interlocked.CompareExchange` on a single continuation field and a completion sentinel, which is far more complex than this workshop needs.

## 5. Wait and Rethrow Correctly

`Wait()` blocks until completion, then rethrows any stored exception while preserving the original stack trace:

```cs
public void Wait()
{
    ManualResetEventSlim? resetEventSlim = null;

    lock (_lock)
    {
        if (!_completed)
        {
            resetEventSlim = new();
            ContinueWith(() => resetEventSlim.Set());
        }
    }

    resetEventSlim?.Wait();

    if (_exception is not null)
    {
        ExceptionDispatchInfo.Throw(_exception);
    }
}
```

## 6. Add Delay

`Delay(TimeSpan)` completes a `CustomTask` when a timer fires:

```cs
public static CustomTask Delay(TimeSpan delay)
{
    CustomTask task = new();

    Timer? timer = null;
    timer = new Timer(_ =>
    {
        timer?.Dispose();
        task.SetResult();
    });

    timer.Change(delay, Timeout.InfiniteTimeSpan);

    return task;
}
```

## 7. Enable Await

`await` works when the awaited type exposes the awaiter pattern: a `GetAwaiter()` method that returns a type with `IsCompleted`, `OnCompleted(Action)` and `GetResult()`. The starter project already contains the awaiter, so the only missing piece is replacing the `GetAwaiter()` stub with a method that returns it:

```cs
public CustomTaskAwaiter GetAwaiter() => new(this);
```

The provided `CustomTaskAwaiter` owns no state of its own. Every member delegates completion and continuation behavior back to `CustomTask`, which is why the starter's `CustomTask.cs` had to declare `IsCompleted`, `ContinueWith(...)` and `Wait()` for `CustomTaskAwaiter.cs` to compile:

```cs
using System.Runtime.CompilerServices;

namespace CoffeeShop;

readonly struct CustomTaskAwaiter : INotifyCompletion
{
    readonly CustomTask _task;

    internal CustomTaskAwaiter(CustomTask task)
    {
        _task = task;
    }

    public bool IsCompleted => _task.IsCompleted;

    public void OnCompleted(Action continuation) => _task.ContinueWith(continuation);

    public CustomTaskAwaiter GetAwaiter() => this;

    public void GetResult() => _task.Wait();
}
```

The coffee shop code in **Steps** needs no changes either. It compiled against the starter's stubs from the beginning, and now every member it calls does real work. Its `await` statements work because the compiler generates the same `IsCompleted`, `OnCompleted(...)` and `GetResult()` calls for `CustomTask` that it generates for `Task`.

There is one difference from `Task` that matters in a Blazor app. `CustomTask` never captures `SynchronizationContext`: `OnCompleted(...)` registers the continuation with `ContinueWith(...)`, and the continuation runs on a thread pool thread. After awaiting a `CustomTask`, a component is off Blazor's renderer, exactly as it is after `ConfigureAwait(false)`, so any component state change must go through `InvokeAsync(...)`. Step 3 checks this.

## 8. Compare Against Finish

Compare your implementation with the completed files:

[2. Finish/CoffeeShop/CustomTask.cs](2.%20Finish/CoffeeShop/CustomTask.cs)

[2. Finish/CoffeeShop/CustomTaskAwaiter.cs](2.%20Finish/CoffeeShop/CustomTaskAwaiter.cs)

[2. Finish/CoffeeShop/Steps](2.%20Finish/CoffeeShop/Steps)

Run the completed app at [http://localhost:5016](http://localhost:5016). Steps 1 to 5 pass as soon as it starts, and Step 6 passes when you run it from its page. Thread IDs will differ on your machine, but every expected result should match:

1. Step 1, print the receipt: `Run()` returns before the receipt is rendered, the render runs on a thread pool thread, and `Wait()` returns on the thread that called it with `IsCompleted` now `True`.
2. Step 2, brew a pour-over: `IsCompleted` is `False` right after `Delay()`, the first continuation runs after the delay, and the second continuation runs only after the first one completes.
3. Step 3, prepare a mobile order: the `async` method returns to its caller at its first `await`, resumes on a thread pool thread after the delay without the `SynchronizationContext` it started on, and awaiting an already completed `CustomTask` keeps running on the same thread.
4. Step 4, wrap the espresso machine's events: `IsCompleted` stays `False` while the shot brews, the `await` resumes only after `ShotPulled`, and completing the same `CustomTask` twice throws `InvalidOperationException`.
5. Step 5, handle a jammed machine and an empty grinder: `await` rethrows the machine's exception, and `Wait()` rethrows the grinder's exception with a stack trace that still starts where it was thrown, not in `CustomTask.Wait()`.
6. Step 6, rush hour: two customers waiting on the same order both resume, a 10,000-link `ContinueWith` chain completes without a stack overflow, and each continuation sees the `ExecutionContext` of the code that registered it, never the context of the thread that completed the `CustomTask`.
