# .NET Internals

In this section, we will inspect the internal data that .NET uses to flow asynchronous state across threads.

## 1. ThreadStatic

1. Using File Explorer (Windows) or Finder (macOS), navigate to **BecomeAnExpertWithAsyncAwait/4. .NET Internals/1. Thread Static**.
1. In the **1. Thread Static** folder, open **ThreadStaticExample.slnx** in your IDE.
1. Open **Program.cs**.
1. Note the `[ThreadStatic]` attribute on `static int _threadSpecificValue`.

   `ThreadStatic` keeps the value of `_threadSpecificValue` isolated to the current thread.

1. Note the local variables `Thread thread1` and `Thread thread2`.

   Both threads run `ThreadMethod()`. Each thread assigns its own value to `_threadSpecificValue` when `Thread.Start()` is called. `Thread.Join()` blocks the calling thread until the target thread completes.

1. Build and run **ThreadStaticExample.csproj**.
1. Confirm that the console output follows this shape:

```console
Main thread - threadSpecificValue: 100
Thread 4 _threadSpecificValue: 51
Thread 5 _threadSpecificValue: 72
Main thread after threads finished - threadSpecificValue: 100
```

Your background thread IDs and random values will differ. The important result is that the main thread keeps its value of `100` before and after the background threads run.

## 2. IPrincipal, Aka Security Context

1. Using File Explorer (Windows) or Finder (macOS), navigate to **BecomeAnExpertWithAsyncAwait/4. .NET Internals/2. Principal**.
1. In the **2. Principal** folder, open **PrincipalExample.slnx** in your IDE.
1. Open **Program.cs**.
1. Note the login controller route:

```cs
app.MapControllerRoute(
    name: "login",
    pattern: "{controller=Account}/{action=Login}/{id?}")
    .WithStaticAssets();
```

1. Open **PrincipalExample/Controllers/AccountController.cs**.
1. Set a breakpoint on the `SignInAsync` statement:

```cs
await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal).ConfigureAwait(ConfigureAwaitOptions.ForceYielding | ConfigureAwaitOptions.None);
```

`ConfigureAwaitOptions.ForceYielding` forces an asynchronous continuation, while `ConfigureAwaitOptions.None` avoids capturing a synchronization context. The continuation may still run on the same physical thread.

1. Set a second breakpoint on the redirect statement:

```cs
return RedirectToAction("Index", "Home");
```

1. Build and debug **PrincipalExample.csproj**.

   Be sure to debug the app, not only run it, and use the Debug build configuration.

1. In your browser, navigate to [http://localhost:5000/Account/Login](http://localhost:5000/Account/Login).
1. Confirm the program pauses on the `SignInAsync` breakpoint.
1. In the debugger, note the current managed thread ID.
1. In the debugger, inspect `HttpContext`.
1. Confirm the `principal` local variable contains the two claims passed into the `ClaimsIdentity`.

   Do not hard-code username and role claims like this in production apps.

1. Resume execution.
1. Confirm the program pauses on the redirect breakpoint.
1. In the debugger, note the current managed thread ID again.

   The current thread ID should be different from the thread ID recorded at the `SignInAsync` breakpoint.

1. Inspect `HttpContext` again.

   The `HttpContext` values should still be available after the thread switch. In .NET Framework 4 and earlier, .NET did not preserve `HttpContext` when switching threads.

## 3. ExecutionContext

1. Using File Explorer (Windows) or Finder (macOS), navigate to **BecomeAnExpertWithAsyncAwait/4. .NET Internals/3. ExecutionContext**.
1. In the **3. ExecutionContext** folder, open **ExecutionContextExample.slnx** in your IDE.
1. Open **Program.cs**.
1. Note the `AsyncLocal<T>` field:

```cs
static readonly AsyncLocal<string> _asyncLocalData = new();
```

`AsyncLocal<T>` flows its value from thread to thread through `ExecutionContext`. This is different from `ThreadStatic`, which keeps data isolated to one physical thread.

1. Set a breakpoint on the first `PrintThreadValues();` call after `_asyncLocalData.Value = "Initial Value";`.
1. Set a breakpoint on the `PrintThreadValues();` call after `Console.WriteLine("Background Thread after assigning values");`.
1. Set a breakpoint on the `PrintThreadValues();` call inside `ExecutionContext.Run(...)`.
1. Set a breakpoint on the `PrintThreadValues();` call after `Console.WriteLine("Main Thread Values");`.
1. Set a breakpoint on the `PrintThreadValues();` call inside the first `Task.Run(...)`.
1. Set a breakpoint on the `PrintThreadValues();` call inside the `Task.Run(...)` after `ExecutionContext.SuppressFlow();`.
1. Build and debug **ExecutionContextExample.csproj**.

   Be sure to debug the app, not only run it, and use the Debug build configuration.

1. Confirm the program pauses at the first breakpoint.
1. Confirm the console output includes:

```console
Thread ID: <main-thread-id>
Culture: Spanish (Spain)
Principal: System.Security.Claims.ClaimsPrincipal
AsyncLocalData: Initial Value
```

1. Resume execution.
1. Confirm the program pauses after `Background Thread after assigning values`.
1. Confirm the console output includes:

```console
Thread ID: <explicit-background-thread-id>
Culture: English (United Kingdom)
Principal: ExecutionContextExample.CustomPrincipal
AsyncLocalData: AsyncLocalData in Thread
```

1. Resume execution.
1. Confirm the program pauses inside `ExecutionContext.Run(...)`.
1. Confirm the console output includes:

```console
Thread ID: <explicit-background-thread-id>
Culture: Spanish (Spain)
Principal: System.Security.Claims.ClaimsPrincipal
AsyncLocalData: Initial Value
```

The same explicit background thread now sees the main thread's culture, principal, and async-local value because the captured `ExecutionContext` was supplied to `ExecutionContext.Run(...)`.

1. Resume execution.
1. Confirm the program pauses after `Main Thread Values`.
1. Confirm the console output includes:

```console
Thread ID: <main-thread-id>
Culture: Spanish (Spain)
Principal: System.Security.Claims.ClaimsPrincipal
AsyncLocalData: Initial Value
```

1. Resume execution.
1. Confirm the program pauses inside the first `Task.Run(...)`.
1. Confirm the console output includes:

```console
Thread ID: <task-run-thread-id>
Culture: Spanish (Spain)
Principal: System.Security.Claims.ClaimsPrincipal
AsyncLocalData: Initial Value
```

`Task.Run(...)` uses a background thread, but async/await automatically flows `ExecutionContext`, so the culture, principal, and async-local value are preserved.

1. Resume execution.
1. Confirm the program pauses inside the `Task.Run(...)` after `ExecutionContext.SuppressFlow();`.
1. Confirm the console output includes:

```console
Thread ID: <suppressed-task-run-thread-id>
Culture: <machine-default-culture>
Principal:
AsyncLocalData:
```

Your managed thread IDs and machine default culture may differ. The important result is that the culture returns to the machine default, `Principal` is empty, and `AsyncLocalData` is empty because `ExecutionContext` flow was suppressed.

## 4. SynchronizationContext

1. Using File Explorer (Windows) or Finder (macOS), navigate to **BecomeAnExpertWithAsyncAwait/4. .NET Internals/4. SynchronizationContext**.
1. In the **4. SynchronizationContext** folder, open **HackerNews.slnx** in your IDE.
1. Open **HackerNews/Components/Pages/News.razor.cs**.
1. Set a breakpoint on this line inside `RefreshAsync(CancellationToken token)`:

```cs
var synchronizationContext = SynchronizationContext.Current;
```

1. Set a second breakpoint inside the `await foreach` loop:

```cs
var synchronizationContextAfterConfigureAwaitFalse = SynchronizationContext.Current;
```

1. Build and debug **HackerNews.csproj**.

   Be sure to debug the app, not only run it, and use the Debug build configuration.

1. Open [http://localhost:5004](http://localhost:5004).
1. Confirm the program pauses on the first breakpoint.
1. In the debugger, confirm `synchronizationContext` is not `null`.

   In Blazor Server, the synchronization context is a renderer/circuit synchronization context. It is not a native UI thread, and the managed thread ID does not have to be `1`.

1. Resume execution.
1. Confirm the code hits the second breakpoint.
1. In the debugger, observe `synchronizationContextAfterConfigureAwaitFalse`. It is commonly `null` after an asynchronous continuation, but it may remain non-null if the awaited operation completed synchronously.

   `ConfigureAwait(false)` and `ConfigureAwaitOptions.None` avoid capturing the synchronization context when a continuation is scheduled. The sample uses `InvokeAsync(...)` to marshal UI state updates back through Blazor's renderer.
