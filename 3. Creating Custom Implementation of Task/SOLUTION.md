# Solution Walkthrough: Creating Custom Implementation of Task

Use this during the guided walkthrough after the challenge and group review in [README.md](README.md).

The completed project is **2. Finish/CreatingTaskFromScratch**. Compare your implementation with the finished sample as we rebuild the solution step by step.

## 1. Track Completion State

`CustomTask` needs shared state that can be read and updated safely across threads:

```cs
readonly Lock _lock = new();

bool _completed;
Action? _action;
Exception? _exception;
ExecutionContext? _context;
```

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

`ContinueWith(Action)` stores a continuation if the task has not completed yet. If the task is already complete, it queues the continuation immediately:

```cs
public CustomTask ContinueWith(Action action)
{
    CustomTask task = new();

    lock (_lock)
    {
        if (_completed)
        {
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
        }
        else
        {
            _action = action;
            _context = ExecutionContext.Capture();
        }
    }

    return task;
}
```

Capturing `ExecutionContext` lets the continuation observe the caller's async-local state, culture, and principal.

## 4. Complete the Task Once

`SetResult()` and `SetException(Exception)` can share the same completion logic:

```cs
public void SetResult() => CompleteTask(null);

public void SetException(Exception exception) => CompleteTask(exception);
```

The shared completion method marks the task complete, stores any exception, and invokes the continuation under the captured `ExecutionContext`:

```cs
void CompleteTask(Exception? exception)
{
    lock (_lock)
    {
        if (_completed)
        {
            throw new InvalidOperationException($"{nameof(CustomTask)} already completed. Cannot complete an already completed {nameof(CustomTask)}");
        }

        _completed = true;
        _exception = exception;

        if (_action is not null)
        {
            if (_context is null)
            {
                _action.Invoke();
            }
            else
            {
                ExecutionContext.Run(_context, state => ((Action?)state)?.Invoke(), _action);
            }
        }
    }
}
```

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

    new Timer(_ => task.SetResult()).Change(delay, Timeout.InfiniteTimeSpan);

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
