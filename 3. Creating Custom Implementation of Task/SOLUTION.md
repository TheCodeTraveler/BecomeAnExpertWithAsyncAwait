# Solution Walkthrough: Creating Custom Implementation of Task

Use this during the guided walkthrough after the challenge and group review in [README.md](README.md).

The completed project is **2. Finish/CreatingTaskFromScratch**. Compare your implementation with the finished sample as we rebuild the solution step by step.

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

The shared completion method marks the antecedent task complete, stores any exception, and drains every registered continuation, running each under its captured `ExecutionContext`:

```cs
void CompleteTask(Exception? exception)
{
    List<(Action Continuation, ExecutionContext? Context)> continuationsToRun;

    lock (_lock)
    {
        if (_completed)
        {
            throw new InvalidOperationException($"{nameof(CustomTask)} already completed. Cannot complete an already completed {nameof(CustomTask)}");
        }

        _completed = true;
        _exception = exception;

        continuationsToRun = [.. _continuations];
        _continuations.Clear();
    }

    // Run outside the lock so continuations can safely interact with this task
    foreach (var (continuation, context) in continuationsToRun)
    {
        if (context is null)
        {
            continuation.Invoke();
        }
        else
        {
            ExecutionContext.Run(context, state => ((Action?)state)?.Invoke(), continuation);
        }
    }
}
```

Copy the list and clear it inside the lock, then invoke outside the lock. Because `_completed` is already `true`, any continuation registered while draining takes the completed branch of `ContinueWith(...)` and is queued directly, so nothing is lost.

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

`await` works when the awaited type exposes the awaiter pattern:

```cs
public CustomTaskAwaiter GetAwaiter() => new(this);
```

The awaiter delegates completion and continuation behavior back to `CustomTask`:

```cs
using System.Runtime.CompilerServices;

namespace CreatingTaskFromScratch;

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

## 8. Compare Against Finish

Compare your implementation with the completed files:

[2. Finish/CreatingTaskFromScratch/CustomTask.cs](2.%20Finish/CreatingTaskFromScratch/CustomTask.cs)

[2. Finish/CreatingTaskFromScratch/CustomTaskAwaiter.cs](2.%20Finish/CreatingTaskFromScratch/CustomTaskAwaiter.cs)

[2. Finish/CreatingTaskFromScratch/Program.cs](2.%20Finish/CreatingTaskFromScratch/Program.cs)

Run the completed program and confirm it prints the starting thread ID followed by three `CustomTask` thread IDs.
