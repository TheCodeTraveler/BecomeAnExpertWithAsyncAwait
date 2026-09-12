# Solution Walkthrough: .NET Internals

Use this during the guided walkthrough after the investigation challenges and group review in [README.md](README.md).

## 1. ThreadStatic

Open **1. Thread Static/ThreadStaticExample/Program.cs** and inspect the `[ThreadStatic]` field:

```cs
[ThreadStatic]
static int _threadSpecificValue;
```

Run the project:

```console
dotnet run --project "1. Thread Static/ThreadStaticExample/ThreadStaticExample.csproj"
```

Expected output shape:

```console
Main thread - threadSpecificValue: 100
Thread 4 _threadSpecificValue: 51
Thread 5 _threadSpecificValue: 72
Main thread after threads finished - threadSpecificValue: 100
```

The exact background thread IDs and random values will differ. The important observation is that each thread has its own value, and the main thread keeps `100` after the background threads complete.

## 2. ExecutionContext

Open **2. ExecutionContext/ExecutionContextExample/Program.cs**.

The main thread sets values controlled by `ExecutionContext`:

```cs
CultureInfo.CurrentCulture = new CultureInfo("es-ES");
Thread.CurrentPrincipal = new ClaimsPrincipal();
_asyncLocalData.Value = "Initial Value";
```

When the sample captures the main thread context and runs it on a background thread, the background thread sees the captured main-thread values:

```cs
var mainThreadExecutionContext = ExecutionContext.Capture() ?? throw new InvalidOperationException("ExecutionContext only null when suppressed");

ExecutionContext.Run(mainThreadExecutionContext, _ =>
{
    Console.WriteLine("Same Background Thread, but using MainThread's ExecutionContext");
    PrintThreadValues();
}, null);
```

When `Task.Run(...)` is awaited normally, `ExecutionContext` flows automatically:

```cs
await Task.Run(() =>
{
    Console.WriteLine("Print Values from Task.Run()");
    PrintThreadValues();
});
```

When flow is suppressed, scope the suppression only around task creation:

```cs
Task suppressedExecutionContextTask;
using (ExecutionContext.SuppressFlow())
{
    suppressedExecutionContextTask = Task.Run(() =>
    {
        Console.WriteLine("Print Values from Task.Run() With Execution Context Suppressed");
        PrintThreadValues();
    });
}

await suppressedExecutionContextTask;
```

`ExecutionContext.SuppressFlow()` returns a thread-affine `AsyncFlowControl`. Create the task while flow is suppressed, leave the `using` block so flow is restored on the current thread, and only then await the task.

Expected suppressed-flow output shape:

```console
Print Values from Task.Run() With Execution Context Suppressed
Thread ID: 7
Culture: <machine-default culture> (eg "English (United States)")
Principal:
AsyncLocalData:
```

The exact thread ID may differ. The important observation is that the culture returns to the machine default, `Principal` is empty, and `AsyncLocalData` is empty.

## 3. Principal

The previous sample showed `ExecutionContext` flowing through a console app. This sample moves the same mechanism into an ASP.NET Core request. The goal is to separate two things that are easy to confuse: values that flow across an `await` because they ride on `ExecutionContext`, and values that are available after an `await` simply because they are object references the code already holds.

Open **3. Principal/PrincipalExample/Controllers/AccountController.cs**. `Login()` sets `Thread.CurrentPrincipal`, then logs four values at three points:

```cs
Thread.CurrentPrincipal = principal;

LogAmbientState("Before await");

await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal).ConfigureAwait(ConfigureAwaitOptions.ForceYielding | ConfigureAwaitOptions.None);

LogAmbientState("After await");

Task suppressedFlowTask;
using (ExecutionContext.SuppressFlow())
{
    suppressedFlowTask = Task.Run(() => LogAmbientState("Inside Task.Run with ExecutionContext suppressed"));
}

await suppressedFlowTask;
```

`ConfigureAwaitOptions.ForceYielding` guarantees an asynchronous continuation, so the "After await" line always runs as a scheduled continuation rather than synchronously.

Debug **PrincipalExample.csproj**, navigate to [http://localhost:5000/Account/Login](http://localhost:5000/Account/Login), and read the three log lines. The output has this shape:

```console
Before await | Thread 12 | Thread.CurrentPrincipal: testuser | IHttpContextAccessor.HttpContext: available | Controller.HttpContext: available | principal local: testuser
After await | Thread 13 | Thread.CurrentPrincipal: testuser | IHttpContextAccessor.HttpContext: available | Controller.HttpContext: available | principal local: testuser
Inside Task.Run with ExecutionContext suppressed | Thread 9 | Thread.CurrentPrincipal: <null> | IHttpContextAccessor.HttpContext: <null> | Controller.HttpContext: available | principal local: testuser
```

Thread IDs will differ. Read the columns, not the rows:

| Value | After await | Flow suppressed | Mechanism |
| --- | --- | --- | --- |
| `Thread.CurrentPrincipal` | available | `<null>` | `ExecutionContext` (AsyncLocal-backed) |
| `IHttpContextAccessor.HttpContext` | available | `<null>` | `ExecutionContext` (AsyncLocal-backed) |
| `Controller.HttpContext` | available | available | Object reference on the controller instance |
| `principal` local | available | available | Object reference hoisted into the async state machine |

The first two columns tell the `ExecutionContext` story. `Thread.CurrentPrincipal` and `IHttpContextAccessor.HttpContext` are both implemented with `AsyncLocal<T>`. They survive the thread switch at "After await" because `await` captured `ExecutionContext` and restored it on the continuation thread. They disappear inside the suppressed `Task.Run(...)` because nothing carried them there.

The last two columns are the misconception to correct. The controller's `HttpContext` property and the `principal` local are still available with flow suppressed because they were never ambient thread state. `this.HttpContext` is a field read on the controller object, and `principal` is a local that the compiler hoisted into the async state machine. Both are ordinary references that any code holding the object can read, regardless of thread or `ExecutionContext`. Observing them after an `await` demonstrates that the state machine kept its captured variables, not that .NET flowed a security context.

This distinction matters in .NET Framework history, too. Before .NET 4.6, `HttpContext.Current` was thread-bound, so it was lost after a thread switch. Modern ASP.NET Core makes `IHttpContextAccessor` AsyncLocal-backed so it flows the same way `Thread.CurrentPrincipal` does.

## 4. SynchronizationContext

Open **4. SynchronizationContext/HackerNews/Components/Pages/News.razor.cs**.

Set a breakpoint before the first await in `RefreshAsync(CancellationToken token)`:

```cs
var synchronizationContext = SynchronizationContext.Current;
```

Set another breakpoint after `ConfigureAwait(false)` resumes inside the `await foreach` loop:

```cs
var synchronizationContextAfterConfigureAwaitFalse = SynchronizationContext.Current;
```

Debug **HackerNews.csproj** and open [http://localhost:5004](http://localhost:5004).

At the first breakpoint, inspect the current thread and `synchronizationContext`. In Blazor Server, the synchronization context is a renderer/circuit synchronization context. It is not a native UI thread, and the managed thread ID does not have to be `1`.

At the second breakpoint, inspect the continuation thread and `synchronizationContextAfterConfigureAwaitFalse`. It is commonly `null` after an asynchronous continuation, but it may remain non-null if the awaited operation completed synchronously.

The key observation is that `ConfigureAwait(false)` and `ConfigureAwaitOptions.None` avoid capturing the synchronization context when a continuation is scheduled. The sample uses `InvokeAsync(...)` to marshal UI state updates back through Blazor's renderer.
