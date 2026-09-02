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

## 2. Principal

Open **2. Principal/PrincipalExample/Program.cs** and inspect the login controller route:

```cs
app.MapControllerRoute(
    name: "login",
    pattern: "{controller=Account}/{action=Login}/{id?}")
    .WithStaticAssets();
```

Open **2. Principal/PrincipalExample/Controllers/AccountController.cs**.

Set a breakpoint on the sign-in await:

```cs
await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal).ConfigureAwait(ConfigureAwaitOptions.ForceYielding | ConfigureAwaitOptions.None);
```

Set another breakpoint on the redirect:

```cs
return RedirectToAction("Index", "Home");
```

Debug **PrincipalExample.csproj** and navigate to [http://localhost:5000/Account/Login](http://localhost:5000/Account/Login).

At the first breakpoint, record the managed thread ID and inspect `HttpContext`, `principal`, and its claims. Resume execution. At the second breakpoint, record the thread ID again and inspect `HttpContext` again.

The current thread ID should change, but the request context and claims remain available. That is the security-context lesson: modern .NET preserves this data across the async continuation.

## 3. ExecutionContext

Open **3. ExecutionContext/ExecutionContextExample/Program.cs**.

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
Culture: English (United States)
Principal:
AsyncLocalData:
```

The exact thread ID may differ. The important observation is that the culture returns to the machine default, `Principal` is empty, and `AsyncLocalData` is empty.

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
