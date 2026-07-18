# Creating Custom Implementation of Task

In this section, we will create our own custom awaitable `CustomTask`.

## 1. Open **CreatingTaskFromScratch.slnx** in IDE

1. Using File Explorer (Windows) / Finder (macOS), navigate to **BecomeAnExpertWithAsyncAwait/3. Creating Custom Implementation of Task/1. Start**
2. In the **1. Start** folder, open **CreatingTaskFromScratch.slnx** in your IDE (Visual Studio on Windows or JetBrains Rider on macOS)

<img width="1028" height="671" alt="image" src="https://github.com/user-attachments/assets/a8495399-19a4-4fca-9eaf-a07a293be206" />

## 2. Recreating **Task.Run(Action)**

1. In your IDE, open **CreatingTaskFromScratch/CustomTask.cs**.
2. Replace the contents of **CustomTask.cs** with this code:

```cs
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace CreatingTaskFromScratch;

sealed class CustomTask
{
	readonly Lock _lock = new();

	bool _completed;
	Action? _action;
  Exception? _exception;
	ExecutionContext? _context;
}
```

3. Replace the contents of **CustomTask.cs** with this updated version that adds the `IsCompleted` property:

```cs
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace CreatingTaskFromScratch;

sealed class CustomTask
{
	readonly Lock _lock = new();

	bool _completed;
	Action? _action;
  Exception? _exception;
	ExecutionContext? _context;
	
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
}
```
> **Note:** The `lock` is required in `IsCompleted` to avoid Race Conditions where one Thread may read its value while another Thread simultaneously modifies its value

4. In **CustomTask.cs**, inside the `CustomTask` class and directly below the `IsCompleted` property, add `public void SetResult()` and `public void SetException(Exception)`:

```cs
public void SetResult()
{
  lock (_lock)
  {
    if (_completed)
      throw new InvalidOperationException($"{nameof(CustomTask)} already completed. Cannot set result of a completed {nameof(CustomTask)}");

    _completed = true;
  }
}

public void SetException(Exception exception)
{  
  lock (_lock)
  {
    if (_completed)
      throw new InvalidOperationException($"{nameof(CustomTask)} already completed. Cannot set exception of a completed {nameof(CustomTask)}");
    
    _completed = true;
    _exception = exception;
  }
}
```

5. In **CustomTask.cs**, inside the `CustomTask` class and directly below `SetException(Exception)`, add `public static CustomTask Run(Action)`:

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
> **Note:** `ThreadPool.QueueUserWorkItem()` queues a method for execution. The method executes when a thread pool thread becomes available.

6. In your IDE, open **CreatingTaskFromScratch/Program.cs**.
7. Replace the contents of **Program.cs** with this code:

```cs
using CreatingTaskFromScratch;

Console.WriteLine($"Starting Thread Id: {Environment.CurrentManagedThreadId}");

CustomTask.Run(() => Console.WriteLine($"First {nameof(CustomTask)} Thread Id: {Environment.CurrentManagedThreadId}"));

Console.ReadLine();
```
> **Note:** We add `Console.ReadLine();` because we cannot yet `await CustomTask.Run()`
8. In your IDE, Build + Run the program, or run this command from **3. Creating Custom Implementation of Task/1. Start**:

```console
dotnet run --project CreatingTaskFromScratch/CreatingTaskFromScratch.csproj
```

9. In your IDE, in the Console output, confirm that the **Starting Thread** ID and the **First CustomTask Thread Id** are different

## 3. Add **.ContinueWith()**

1. In your IDE, open **CreatingTaskFromScratch/CustomTask.cs**.
2. In **CustomTask.cs**, inside the `CustomTask` class and directly below `Run(Action)`, add `public CustomTask ContinueWith(Action)`:

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
> **Note:** `ExecutionContext` stores the state of the calling thread, including its CultureInfo and IPrincipal (security information)

3. In **CustomTask**, replace the existing `void SetResult()` and `void SetException(Exception)` methods with the following two expression-bodied methods, and then add the new helper method `void CompleteTask(Exception?)` directly below them:
```cs
public void SetResult() => CompleteTask(null);

public void SetException(Exception exception) => CompleteTask(exception);

void CompleteTask(Exception? exception)
{
  lock (_lock)
  {
    if (_completed)
      throw new InvalidOperationException($"{nameof(CustomTask)} already completed. Cannot complete an already completed {nameof(CustomTask)}");

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
        ExecutionContext.Run(_context, state => ((Action?)state)?.Invoke() , _action);
      }
    }
  }
}
```
4. In your IDE, open **CreatingTaskFromScratch/Program.cs**.
5. Replace the `CustomTask.Run(...)` statement in **Program.cs** with this statement:
```cs
CustomTask.Run(() => Console.WriteLine($"First {nameof(CustomTask)} Thread Id: {Environment.CurrentManagedThreadId}"))
  .ContinueWith(() => Console.WriteLine($"Second {nameof(CustomTask)} Thread Id: {Environment.CurrentManagedThreadId}"));
```
6. In your IDE, Build + Run the program.
7. In your IDE, in the Console output, confirm that the **First CustomTask Thread Id** is the same as **Second CustomTask Thread Id**.

## 4. Add **Wait()**

1. In your IDE, open **CreatingTaskFromScratch/CustomTask.cs**.
2. In **CustomTask.cs**, inside the `CustomTask` class and directly below `Run(Action)`, add `public void Wait()`:
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
> **Note:** **ManualResetEventSlim** is used by .NET to manage a Thread's waiting behavior
>
> **Note:** **ManualResetEventSlim.Wait()** is a blocking call

3. In your IDE, open **CreatingTaskFromScratch/Program.cs**.
4. Replace the contents of **Program.cs** with this code, which uses **.Wait()** instead of **Console.ReadLine()**:
```cs
using CreatingTaskFromScratch;

Console.WriteLine($"Starting Thread Id: {Environment.CurrentManagedThreadId}");

CustomTask.Run(() => Console.WriteLine($"First {nameof(CustomTask)} Thread Id: {Environment.CurrentManagedThreadId}")).Wait();

Console.WriteLine($"Second {nameof(CustomTask)} Id: {Environment.CurrentManagedThreadId}");

CustomTask.Run(() => Console.WriteLine($"Third {nameof(CustomTask)} Thread Id: {Environment.CurrentManagedThreadId}")).Wait();
```
5. In your IDE, Build + Run the program
6. In your IDE, in the Console output, confirm that the **Second CustomTask Thread Id** is the same as the **Starting Thread Id**
7. In your IDE, in the Console output, confirm that the **First CustomTask Thread Id** and **Third Thread Id** are background threads (aka not Thread 1)
> **Note:** **First CustomTask Thread Id** and **Third CustomTask Thread Id** may use the same background thread because **ThreadPool.QueueUserWorkItem** grabs any Thread that is available in the Thread Pool and the Thread used for **First CustomTask Thread Id** may be reused for **Third CustomTask Id**

## 5. Add **.Delay(TimeSpan)**

1. In your IDE, open **CreatingTaskFromScratch/CustomTask.cs**.
2. In **CustomTask.cs**, inside the `CustomTask` class and directly below `IsCompleted`, add **public static CustomTask Delay(TimeSpan)**:

```cs
public static CustomTask Delay(TimeSpan delay)
{
  CustomTask task = new();

  new Timer(_ => task.SetResult()).Change(delay, Timeout.InfiniteTimeSpan);

  return task;
}
```
> **Note:** Passing **Timeout.InfiniteTimeSpan** into **Timer.Change()** disables the periodic function of **Timer**

3. In your IDE, open **CreatingTaskFromScratch/Program.cs**.
4. Replace the contents of **Program.cs** with this code, which adds calls to **CustomTask.Delay(TimeSpan)**:
```cs
using CreatingTaskFromScratch;

Console.WriteLine($"Starting Thread Id: {Environment.CurrentManagedThreadId}");

CustomTask.Run(() => Console.WriteLine($"First {nameof(CustomTask)} Thread Id: {Environment.CurrentManagedThreadId}")).Wait();

CustomTask.Delay(TimeSpan.FromSeconds(1)).Wait();

Console.WriteLine($"Second {nameof(CustomTask)} Thread Id: {Environment.CurrentManagedThreadId}");

CustomTask.Delay(TimeSpan.FromSeconds(1)).Wait();

CustomTask.Run(() => Console.WriteLine($"Third {nameof(CustomTask)} Thread Id: {Environment.CurrentManagedThreadId}")).Wait();
```
5. In your IDE, Build + Run the program
6. Confirm that the Program no longer finishes executing near-instantly thanks to **CustomTask.Delay(TimeSpan)**

## 6. Enable **await** Keyword

1. In your IDE, in the **CreatingTaskFromScratch** project, add a new file called **CustomTaskAwaiter.cs**

<img width="1064" height="304" alt="Screenshot 2026-01-23 at 1 59 37 PM" src="https://github.com/user-attachments/assets/d8e03b9c-9d5e-4c61-8ef0-07d40bab31e3" />


2. In **CustomTaskAwaiter.cs**, paste this code:

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
3. In your IDE, open **CreatingTaskFromScratch/CustomTask.cs**.
4. In **CustomTask.cs**, inside the `CustomTask` class and directly above `SetResult()`, add **public CustomTaskAwaiter GetAwaiter()**:

```cs
public CustomTaskAwaiter GetAwaiter() => new(this);
```
5. In your IDE, open **CreatingTaskFromScratch/Program.cs**.
6. Replace the contents of **Program.cs** with this code, which replaces **.Wait()** with **await**:

```cs
using CreatingTaskFromScratch;

Console.WriteLine($"Starting Thread Id: {Environment.CurrentManagedThreadId}");

await CustomTask.Run(() => Console.WriteLine($"First {nameof(CustomTask)} Id: {Environment.CurrentManagedThreadId}"));

await CustomTask.Delay(TimeSpan.FromSeconds(1));

Console.WriteLine($"Second {nameof(CustomTask)} Id: {Environment.CurrentManagedThreadId}");

await CustomTask.Delay(TimeSpan.FromSeconds(1));

await CustomTask.Run(() => Console.WriteLine($"Third {nameof(CustomTask)} Id: {Environment.CurrentManagedThreadId}"));
```
> **Note:** The .NET Compiler uses [Duck Typing](https://en.wikipedia.org/wiki/Duck_typing) to enable the **await** keyword. Any object can be awaited as long as it exposes a `GetAwaiter()` method whose awaiter implements the expected awaiter pattern.
7. In your IDE, Build + Run the program
