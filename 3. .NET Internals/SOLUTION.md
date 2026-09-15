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

Open **3. Principal/PrincipalExample/Controllers/HomeController.cs**. `RunExperiment()` holds the signed-in user in a local variable, then records four checkpoints:

```cs
var checkpoints = new List<Checkpoint>();
var signedInUser = HttpContext.User;

checkpoints.Add(Observe("1. Start of the action"));

Thread.CurrentPrincipal = signedInUser;

// Yields the current thread: the rest of this method runs later as a continuation on the thread pool
await Task.Yield();

checkpoints.Add(Observe("2. After await Task.Yield()"));

checkpoints.Add(await Task.Run(() => Observe("3. Inside Task.Run(...)")));

Task<Checkpoint> suppressedFlowTask;
using (ExecutionContext.SuppressFlow())
{
    suppressedFlowTask = Task.Run(() => Observe("4. Inside Task.Run(...) started while ExecutionContext flow is suppressed"));
}

checkpoints.Add(await suppressedFlowTask);
```

Each checkpoint calls the `Observe(string)` local function, which reads the current thread ID and asks for the signed-in user's name four different ways:

```cs
Checkpoint Observe(string checkpoint) => new Checkpoint(
    checkpoint,
    Environment.CurrentManagedThreadId,
    Thread.CurrentPrincipal?.Identity?.Name,
    httpContextAccessor.HttpContext?.User.Identity?.Name,
    HttpContext.User.Identity?.Name,
    signedInUser.Identity?.Name);
```

`await Task.Yield()` never completes synchronously. ASP.NET Core has no `SynchronizationContext`, so the rest of the method is queued to the thread pool as a real continuation, and it usually resumes on a different thread.

Run the project:

```console
dotnet run --project "3. Principal/PrincipalExample/PrincipalExample.csproj"
```

Open [http://localhost:5000](http://localhost:5000), sign in, and select **Run the experiment**. Signed in as `Ada`, one run produced this **Results** table:

| Checkpoint | Thread | `Thread.CurrentPrincipal` | `httpContextAccessor.HttpContext?.User` | `HttpContext.User` | `signedInUser` |
| --- | --- | --- | --- | --- | --- |
| 1. Start of the action | 15 | null | Ada | Ada | Ada |
| 2. After await Task.Yield() | 12 | Ada | Ada | Ada | Ada |
| 3. Inside Task.Run(...) | 7 | Ada | Ada | Ada | Ada |
| 4. Inside Task.Run(...) started while ExecutionContext flow is suppressed | 7 | null | null | Ada | Ada |

Your name and the thread IDs will differ, and a checkpoint may reuse a thread from an earlier row. Select **Run it again** a few times: the thread IDs move around, but the names and `null`s never change. Read the columns, not the rows.

### Where the signed-in user comes from

Open **3. Principal/PrincipalExample/Program.cs**. The app uses cookie authentication:

```cs
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(static options => options.LoginPath = "/");

var app = builder.Build();

// Reads the sign-in cookie on every request and assigns the signed-in user to HttpContext.User
app.UseAuthentication();
app.UseAuthorization();
```

When you signed in, `AccountController.Login` called `HttpContext.SignInAsync(...)`, which only writes the sign-in cookie:

```cs
// Writes the sign-in cookie. HttpContext.User is assigned from that cookie on the next request.
await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
```

On every request after that, the authentication middleware turns the cookie back into a `ClaimsPrincipal` and assigns it to `HttpContext.User` before the controller runs. That is why three columns already show your name at checkpoint 1. Nothing in ASP.NET Core assigns `Thread.CurrentPrincipal`, so it is `null` at checkpoint 1, and the sample copies the user into it by hand right after. In ASP.NET Core, `HttpContext.User` (also exposed as the controller's `User` property) is the principal to use. `Thread.CurrentPrincipal` only holds the user if your own code puts it there.

Checkpoint 1 stays `null` on every run, even when the request lands on a thread where an earlier run assigned `Thread.CurrentPrincipal`. The earlier value belonged to that request's `ExecutionContext`, not to the thread, and the thread pool resets each thread to the default context after every work item it runs.

### What `ExecutionContext` carries

`Thread.CurrentPrincipal` and `IHttpContextAccessor.HttpContext` are both implemented with `AsyncLocal<T>`. At checkpoint 2 the thread changed from 15 to 12, yet both columns still show `Ada`: `await` captured `ExecutionContext` and restored it on the continuation thread. `Task.Run(...)` does the same capture and restore at checkpoint 3, on the thread pool thread that runs the lambda.

At checkpoint 4 both columns are `null`. The task was created while flow was suppressed, so no `ExecutionContext` was captured for it, and its lambda ran with the default, empty context. The thread does not decide the result: in the run above, checkpoint 4 ran on thread 7, the same thread as checkpoint 3, and still saw `null`, because `ExecutionContext` belongs to the work item, not to the thread. The `httpContextAccessor` object itself was still reachable at checkpoint 4; only the `AsyncLocal<T>` lookup behind its `HttpContext` property came back empty.

As in the previous sample, `ExecutionContext.SuppressFlow()` returns a thread-affine `AsyncFlowControl`. Create the task inside the `using` block, leave the block so flow is restored on the same thread, and only then await the task. Awaiting inside the block would let the method return before the block ends. The continuation runs with flow no longer suppressed, often on a different thread, so disposing the `AsyncFlowControl` at the end of the block throws `InvalidOperationException` even when the thread happens to be the same.

### What only looks like it flowed

`HttpContext.User` and `signedInUser` show `Ada` in every row, including checkpoint 4. This is the misconception to correct: neither value was ever ambient state.

- `HttpContext` is a controller property that reads `ControllerContext.HttpContext`. MVC assigned `ControllerContext` when it created the controller, so this is a chain of ordinary object references reached through `this`.
- `signedInUser` is a local variable used by the `Observe` local function, so the compiler stores it, along with `this`, in a closure object that the async state machine and both `Task.Run(...)` lambdas reference.

Any code holding those objects can read them, on any thread, whether or not `ExecutionContext` flowed. Seeing them after an `await` proves that the compiler kept its captured variables, not that .NET flowed a security context.

The `httpContextAccessor.HttpContext?.User` and `HttpContext.User` columns reach the same `HttpContext` object in two very different ways: `httpContextAccessor.HttpContext` looks it up in an `AsyncLocal<T>`, while the controller's `HttpContext` property is a plain object reference.

| Value | Checkpoint 1 | Checkpoint 2 (after await) | Checkpoint 4 (flow suppressed) | Mechanism |
| --- | --- | --- | --- | --- |
| `Thread.CurrentPrincipal` | `null` | your name | `null` | `ExecutionContext` (AsyncLocal-backed), assigned only by your code |
| `httpContextAccessor.HttpContext?.User` | your name | your name | `null` | `ExecutionContext` (AsyncLocal-backed) |
| `HttpContext.User` | your name | your name | your name | Object reference on the controller instance |
| `signedInUser` | your name | your name | your name | Local variable captured in a compiler-generated closure |

A bit of history: in classic ASP.NET (System.Web), `HttpContext.Current` relied on ASP.NET's `SynchronizationContext` to be re-attached on the continuation thread, so it was commonly `null` after `ConfigureAwait(false)`. ASP.NET Core's `IHttpContextAccessor` is AsyncLocal-backed instead, so it flows the same way `Thread.CurrentPrincipal` does.

## 4. SynchronizationContext

The previous two samples showed the values that ride on `ExecutionContext`. This sample prints the other piece of ambient state that `await` captures: `SynchronizationContext`. In Blazor Server that is the renderer's synchronization context, and it is exactly what `ConfigureAwait(false)` opts out of.

Open **4. SynchronizationContext/HackerNews/Components/Pages/News.razor.cs**. `RefreshAsync(CancellationToken token)` logs the current thread and `SynchronizationContext` before its first `ConfigureAwait(false)`:

```cs
var thread = Thread.CurrentThread;
var synchronizationContext = SynchronizationContext.Current;
Logger.LogInformation("Before ConfigureAwait(false) | Thread {ThreadId} | SynchronizationContext: {SynchronizationContext}", thread.ManagedThreadId, synchronizationContext?.GetType().Name ?? "<null>");
```

It logs them again each time the `await foreach` loop resumes after `ConfigureAwait(false)`:

```cs
await foreach (var story in GetTopStories(topStoryIds, StoriesConstants.NumberOfStories, token).ConfigureAwait(false))
{
    var threadAfterConfigureAwaitFalse = Thread.CurrentThread;
    var synchronizationContextAfterConfigureAwaitFalse = SynchronizationContext.Current;
    Logger.LogInformation("After ConfigureAwait(false) | Thread {ThreadId} | SynchronizationContext: {SynchronizationContext}", threadAfterConfigureAwaitFalse.ManagedThreadId, synchronizationContextAfterConfigureAwaitFalse?.GetType().Name ?? "<null>");

    await InvokeAsync(() =>
    {
        if (!TopStoryCollection.Any(x => x.Title.Equals(story.Title, StringComparison.Ordinal)))
        {
            InsertIntoSortedList(TopStoryCollection, (a, b) => b.Score.CompareTo(a.Score), story);
        }

        StateHasChanged();
    });
}
```

Run the project:

```console
dotnet run --project "4. SynchronizationContext/HackerNews/HackerNews.csproj"
```

Open [http://localhost:5004](http://localhost:5004) and read the `HackerNews.Components.Pages.NewsPageBase` lines in the console. They are interleaved with ASP.NET Core's request logging, so look for the `NewsPageBase` category. There is one `Before` line per refresh and one `After` line per story, and the output has this shape:

```console
info: HackerNews.Components.Pages.NewsPageBase[0]
      Before ConfigureAwait(false) | Thread 3 | SynchronizationContext: RendererSynchronizationContext
info: HackerNews.Components.Pages.NewsPageBase[0]
      After ConfigureAwait(false) | Thread 19 | SynchronizationContext: <null>
info: HackerNews.Components.Pages.NewsPageBase[0]
      After ConfigureAwait(false) | Thread 24 | SynchronizationContext: <null>
info: HackerNews.Components.Pages.NewsPageBase[0]
      After ConfigureAwait(false) | Thread 3 | SynchronizationContext: <null>
```

Thread IDs will differ. Before the await, `SynchronizationContext.Current` is Blazor's `RendererSynchronizationContext`. It is not a native UI thread, and the managed thread ID does not have to be `1`. Blazor Server has no dedicated UI thread: the renderer's synchronization context runs its work on thread pool threads, one work item at a time, and that context is what serializes access to component state.

After `ConfigureAwait(false)`, each continuation runs on whichever thread pool thread completed the awaited operation, and `SynchronizationContext.Current` is `<null>`. In the run above, thread `3` ran the code before the await and later ran a continuation with no synchronization context at all. The thread is not what changed. `ConfigureAwait(false)` told the awaiter not to capture the context, so nothing restored it when the continuation was scheduled. With fifty continuations you may see the same reuse in your own output.

A non-null `After` line is also possible. If the awaited operation had already completed when the `await` ran, there was no continuation to schedule, so the code kept running on the same thread with the same context. `ConfigureAwait(false)` only affects continuations that are actually scheduled.

The key observation is that `ConfigureAwait(false)` and `ConfigureAwaitOptions.None` avoid capturing the synchronization context when a continuation is scheduled. The sample uses `InvokeAsync(...)` to marshal UI state updates back through Blazor's renderer.
