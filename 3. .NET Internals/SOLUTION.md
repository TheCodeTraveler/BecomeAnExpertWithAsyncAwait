# Solution Walkthrough: .NET Internals

Use this during the guided walkthrough after the investigation challenges and group review in [README.md](README.md).

All four experiments live in one app, **InternalsLab**. There is no Start or Finish folder, because nothing is broken: every section below walks through one step's experiment, the results it produces, and the observations behind that step's explain questions.

Run the app:

```console
dotnet run --project "3. .NET Internals/InternalsLab/InternalsLab.csproj"
```

Open [http://localhost:5003](http://localhost:5003). Your thread IDs, random values, and name will differ from the runs shown below. The predictions each step grades never do.

## 1. ThreadStatic (Step 1)

Open **InternalsLab/Experiments/ThreadStaticExperiment.cs** and inspect the `[ThreadStatic]` field:

```cs
[ThreadStatic]
static int _threadSpecificValue;
```

The Step 1 page runs `RunAsync()` on a new dedicated thread that plays the part of a console app's main thread. The main thread assigns `100`, starts two background threads that each record the field before and after assigning a random value, waits for both with `Join()`, and then awaits `Task.Yield()`:

```cs
await Task.Yield();

// Step 1: checkpoint 7. Whichever thread pool thread runs the continuation
log.Record(7, _threadSpecificValue);
```

One run produced these results:

| Checkpoint | Thread | Pool thread | `_threadSpecificValue` |
| --- | --- | --- | --- |
| 1. Main thread, after assigning 100 | 23 | no | 100 |
| 2. Background thread 1, before assigning | 24 | no | 0 |
| 3. Background thread 1, after assigning its random value | 24 | no | 7 |
| 4. Background thread 2, before assigning | 25 | no | 0 |
| 5. Background thread 2, after assigning its random value | 25 | no | 93 |
| 6. Main thread, after both threads finish | 23 | no | 100 |
| 7. After await Task.Yield() | 9 | yes | 0 |

The exact background thread IDs and random values will differ. The important observation is that each thread has its own value, and the main thread keeps `100` after the background threads complete.

Checkpoints 2 and 4 show that nothing copies the main thread's value into a new thread: each thread's copy starts at `0`, the default for `int`. Checkpoint 7 is the reason async code cannot use `[ThreadStatic]` for ambient data. `Task.Yield()` always schedules a continuation, the dedicated thread has no `SynchronizationContext`, so the continuation ran on a thread pool thread, and that thread has its own copy of the field. The value stayed behind with the thread. The next step shows the storage .NET uses instead.

## 2. ExecutionContext (Step 2)

Open **InternalsLab/Experiments/ExecutionContextExperiment.cs**.

The main thread sets values controlled by `ExecutionContext`:

```cs
CultureInfo.CurrentCulture = new CultureInfo("es-ES");
Thread.CurrentPrincipal = new ClaimsPrincipal();
_asyncLocalData.Value = "Initial Value";
```

When the sample captures the main thread context and runs it on a background thread, the background thread sees the captured main-thread values:

```cs
var mainThreadExecutionContext = ExecutionContext.Capture() ?? throw new InvalidOperationException("ExecutionContext only null when suppressed");
```

```cs
ExecutionContext.Run(mainThreadExecutionContext, _ =>
{
    // Step 2: checkpoint 3. The same background thread, but running with the main thread's ExecutionContext
    RecordThreadValues(3);
}, null);
```

When `Task.Run(...)` is awaited normally, `ExecutionContext` flows automatically:

```cs
await Task.Run(() =>
{
    // Step 2: checkpoint 5. Inside Task.Run()
    RecordThreadValues(5);
});
```

When flow is suppressed, scope the suppression only around task creation:

```cs
Task suppressedExecutionContextTask;
using (ExecutionContext.SuppressFlow())
{
    suppressedExecutionContextTask = Task.Run(() =>
    {
        // Step 2: checkpoint 6. Inside Task.Run() started while ExecutionContext flow is suppressed
        RecordThreadValues(6);
    });
}

// Step 2: the task is created inside the using block, but awaited only after the block ends
await suppressedExecutionContextTask;
```

`ExecutionContext.SuppressFlow()` returns a thread-affine `AsyncFlowControl`. Create the task while flow is suppressed, leave the `using` block so flow is restored on the current thread, and only then await the task.

One run produced these results:

| Checkpoint | Thread | Culture | Principal | AsyncLocal |
| --- | --- | --- | --- | --- |
| 1. Main thread, after assigning its values | 21 | es-ES | ClaimsPrincipal | Initial Value |
| 2. Background thread, after assigning its own values | 22 | en-GB | CustomPrincipal | AsyncLocalData in Thread |
| 3. Same background thread, inside ExecutionContext.Run(mainThreadExecutionContext, ...) | 22 | es-ES | ClaimsPrincipal | Initial Value |
| 4. Main thread, after the background thread finishes | 21 | es-ES | ClaimsPrincipal | Initial Value |
| 5. Inside Task.Run(...) | 9 | es-ES | ClaimsPrincipal | Initial Value |
| 6. Inside Task.Run(...) started while ExecutionContext flow is suppressed | 7 | en-US | null | null |

The exact thread IDs may differ. At checkpoint 6, the important observation is that the culture returns to the machine default (`en-US` on this machine), `Principal` is empty, and `AsyncLocalData` is empty.

Checkpoint 3 runs on the same thread as checkpoint 2, yet sees the main thread's values: `ExecutionContext.Run(...)` applies the captured context only for the duration of the callback. Checkpoint 4 shows that the background thread's assignments changed its own `ExecutionContext`, never the main thread's. Checkpoints 5 and 6 both run on thread pool threads, and the thread is not what decides the result: `Task.Run(...)` captures `ExecutionContext` when the task is created, and inside `SuppressFlow()` there is nothing to capture.

The app starts the experiment with the same pattern, which is how the values the experiment assigns never leak into the Blazor circuit, and the circuit's values never leak into the experiment. From **InternalsLab/Steps/WorkshopStep.cs**:

```cs
Task experimentTask;
using (ExecutionContext.SuppressFlow())
{
    experimentTask = Task.Factory.StartNew(experiment, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
}

await experimentTask.WaitAsync(Timeout, token).ConfigureAwait(false);
```

### Try it: await inside the using block

The **Try it** button, which appears below the results on the Step 2 page once the experiment has run, runs `AwaitInsideSuppressFlowAsync()`, the version that awaits inside the block:

```cs
public async Task AwaitInsideSuppressFlowAsync()
{
    using (ExecutionContext.SuppressFlow())
    {
        // Step 2: the thread that starts the using block
        RecordThreadValues(1);

        await Task.Run(() => RecordThreadValues(2));

        // Step 2: the thread that runs the continuation, which is also the thread that ends the using block
        RecordThreadValues(3);
    }
}
```

The `await` pauses the method before the `using` block ends, so the rest of the method, including the end of the block, runs later on a thread pool thread. A `using (ExecutionContext.SuppressFlow())` block has to end on the thread that started it, so ending it there throws. The page shows the two thread IDs and the full exception:

```text
System.InvalidOperationException: AsyncFlowControl object must be used on the thread where it was created.
   at System.Threading.AsyncFlowControl.Undo()
   at InternalsLab.ExecutionContextExperiment.AwaitInsideSuppressFlowAsync() in Experiments/ExecutionContextExperiment.cs:line 85
```

In the rare run where the task has already finished when the `await` checks it, the method never pauses, the block ends on the thread that started it, and nothing throws. That does not make the code correct. It makes the bug intermittent.

## 3. Principal (Step 3)

The previous sample showed `ExecutionContext` flowing through a console-style program. This sample moves the same mechanism into an ASP.NET Core request. The goal is to separate two things that are easy to confuse: values that flow across an `await` because they ride on `ExecutionContext`, and values that are available after an `await` simply because they are object references the code already holds.

Open **InternalsLab/Controllers/PrincipalController.cs**. `RunExperiment()` holds the signed-in user in a local variable, then records four checkpoints:

```cs
var checkpoints = new List<Checkpoint>();

// Step 3: an ordinary local variable that holds the signed-in user
var signedInUser = HttpContext.User;

// Step 3: checkpoint 1. Nothing in this action has awaited yet
checkpoints.Add(Observe("1. Start of the action"));

// Try it: delete this line, apply the change with Hot Reload, and click Run it again. Which cells change at checkpoints 2 and 3?
Thread.CurrentPrincipal = signedInUser;

// Yields the current thread: the rest of this method runs later as a continuation on the thread pool
// Try it: replace this line with await Task.Delay(1).ConfigureAwait(false) and apply it with Hot Reload. Does httpContextAccessor still find the HttpContext at checkpoint 2?
await Task.Yield();

// Step 3: checkpoint 2. The continuation, usually on a different thread
checkpoints.Add(Observe("2. After await Task.Yield()"));

// Step 3: checkpoint 3. A thread pool thread running the Task.Run(...) lambda
checkpoints.Add(await Task.Run(() => Observe("3. Inside Task.Run(...)")));

// Step 3: checkpoint 4. The task is created while ExecutionContext flow is suppressed, and awaited only after the using block ends
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

The controller is an ordinary MVC action inside the same app, because the experiment has to observe a real request. **Run the experiment** on the Step 3 page leaves the Blazor circuit with a full page load, and the action saves its checkpoints in the lab notebook and redirects back:

```cs
// Workshop plumbing: saves the checkpoints in the lab notebook, then goes back to the Step 3 page to compare them with your predictions
notebook.RecordPrincipalRun(checkpoints);

return Redirect("/steps/3");
```

Open [http://localhost:5003/steps/3](http://localhost:5003/steps/3), sign in, and select **Run the experiment**. Signed in as `Ada`, one run produced this **Results** table:

| Checkpoint | Thread | `Thread.CurrentPrincipal` | `httpContextAccessor.HttpContext?.User` | `HttpContext.User` | `signedInUser` |
| --- | --- | --- | --- | --- | --- |
| 1. Start of the action | 13 | null | Ada | Ada | Ada |
| 2. After await Task.Yield() | 7 | Ada | Ada | Ada | Ada |
| 3. Inside Task.Run(...) | 15 | Ada | Ada | Ada | Ada |
| 4. Inside Task.Run(...) started while ExecutionContext flow is suppressed | 15 | null | null | Ada | Ada |

Your name and the thread IDs will differ, and a checkpoint may reuse a thread from an earlier row. Select **Run it again** a few times: the thread IDs move around, and the page shows the previous run's thread ID under each one, but the names and `null`s never change. Read the columns, not the rows.

### Where the signed-in user comes from

Open **InternalsLab/Program.cs**. The app uses cookie authentication:

```cs
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(static options => options.LoginPath = "/Account/SignIn");
```

```cs
// Step 3: reads the sign-in cookie on every request and assigns the signed-in user to HttpContext.User
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

`Thread.CurrentPrincipal` and `IHttpContextAccessor.HttpContext` are both implemented with `AsyncLocal<T>`. At checkpoint 2 the thread changed from 13 to 7, yet both columns still show `Ada`: `await` captured `ExecutionContext` and restored it on the continuation thread. `Task.Run(...)` does the same capture and restore at checkpoint 3, on the thread pool thread that runs the lambda.

At checkpoint 4 both columns are `null`. The task was created while flow was suppressed, so no `ExecutionContext` was captured for it, and its lambda ran with the default, empty context. The thread does not decide the result: in the run above, checkpoint 4 ran on thread 15, the same thread as checkpoint 3, and still saw `null`, because `ExecutionContext` belongs to the work item, not to the thread. The `httpContextAccessor` object itself was still reachable at checkpoint 4; only the `AsyncLocal<T>` lookup behind its `HttpContext` property came back empty.

As in the previous sample, a `using (ExecutionContext.SuppressFlow())` block has to end on the thread that started it. Create the task inside the block, let the block end, and only then await the task. An `await` inside the block would pause the method before the block ends, and the end of the block would run later, usually on another thread, where it throws `InvalidOperationException`. It throws even when the continuation happens to land on the same thread, because by then flow is no longer suppressed there. Step 2's **Try it** button shows exactly that.

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

A bit of history: in classic ASP.NET (System.Web), `HttpContext.Current` relied on ASP.NET's `SynchronizationContext` to be re-attached on the continuation thread, so it was commonly `null` after `ConfigureAwait(false)`. ASP.NET Core's `IHttpContextAccessor` is AsyncLocal-backed instead, so it flows the same way `Thread.CurrentPrincipal` does. The second `// Try it:` comment in `RunExperiment()` proves it: replace `await Task.Yield()` with `await Task.Delay(1).ConfigureAwait(false)`, and checkpoint 2 still shows your name in the `httpContextAccessor.HttpContext?.User` column.

## 4. SynchronizationContext (Step 4)

The previous two samples showed the values that ride on `ExecutionContext`. This sample shows the other piece of ambient state that `await` captures: `SynchronizationContext`. In Blazor Server that is the renderer's synchronization context, and it is exactly what `ConfigureAwait(false)` opts out of.

Open **InternalsLab/Experiments/SynchronizationContextExperiment.cs**. `RefreshAsync(Func<Action, Task>, CancellationToken)` is shaped like `RefreshAsync` from Correcting Common Async Await Mistakes, with a simulated story feed in place of Hacker News so the app runs offline. It records `SynchronizationContext.Current` and the thread at six checkpoints:

```cs
public async Task RefreshAsync(Func<Action, Task> invokeAsync, CancellationToken token)
{
    // Step 4: checkpoint 1. Before the first await, still inside the click handler
    RecordSynchronizationContext(1);

    // Step 4: a plain await. GetTopStoryIDs() has to wait for the simulated network.
    var topStoryIds = await _storyFeed.GetTopStoryIDs(token);

    // Step 4: checkpoint 2. After a plain await
    RecordSynchronizationContext(2);

    // Step 4: the top story is already in the feed's cache, so the ValueTask has completed before the await looks at it
    var topStory = await _storyFeed.GetStory(topStoryIds[0], token).ConfigureAwait(false);

    // Step 4: checkpoint 3. After ConfigureAwait(false) on a story that was already cached
    RecordSynchronizationContext(3);

    // Step 4: this story is not cached, so the await has to wait for the simulated network
    // Try it: remove ConfigureAwait(false) from this await and apply the change with Hot Reload. Which checkpoints change?
    var secondStory = await _storyFeed.GetStory(topStoryIds[1], token).ConfigureAwait(false);

    // Step 4: checkpoint 4. After ConfigureAwait(false) on a story that had to download
    RecordSynchronizationContext(4);

    // Step 4: a plain await again, but it runs after checkpoint 4
    var thirdStory = await _storyFeed.GetStory(topStoryIds[2], token);

    // Step 4: checkpoint 5. After a plain await that started where checkpoint 4 left off
    RecordSynchronizationContext(5);

    // Step 4: InvokeAsync() runs the lambda through Blazor's renderer, which is how RefreshAsync() safely changes component state
    await invokeAsync(() =>
    {
        TopStories.AddRange([topStory, secondStory, thirdStory]);

        // Step 4: checkpoint 6. Inside InvokeAsync()
        RecordSynchronizationContext(6);
    });
}
```

**Run the experiment** calls it straight from the Step 4 page's click handler, passing the page's `InvokeAsync`, with nothing awaited first. From **InternalsLab/Steps/Step4SynchronizationContext.cs**:

```cs
// No Task.Run and no ConfigureAwait(false) before this call: RefreshAsync() starts synchronously,
// on Blazor's renderer, exactly like the click handler that called this method
var refreshTask = experiment.RefreshAsync(invokeAsync, token);
```

The top story is served from the feed's cache. Open **InternalsLab/Experiments/SimulatedStoryFeed.cs**:

```cs
// A cached story comes back in a ValueTask that has already completed, so awaiting it never has to wait
public ValueTask<Story> GetStory(long storyId, CancellationToken token)
{
    if (_cache.TryGetValue(storyId, out var cachedStory))
        return ValueTask.FromResult(cachedStory);

    return new ValueTask<Story>(DownloadStory(storyId, token));
}
```

One run produced these results:

| Checkpoint | Thread | Pool thread | `SynchronizationContext.Current` |
| --- | --- | --- | --- |
| 1. Before the first await, in the click handler | 19 | yes | RendererSynchronizationContext |
| 2. After a plain await on GetTopStoryIDs() | 18 | yes | RendererSynchronizationContext |
| 3. After ConfigureAwait(false) on a story that was already cached | 18 | yes | RendererSynchronizationContext |
| 4. After ConfigureAwait(false) on a story that had to download | 18 | yes | null |
| 5. After a plain await that started where checkpoint 4 left off | 18 | yes | null |
| 6. Inside InvokeAsync(...) | 18 | yes | RendererSynchronizationContext |

Thread IDs will differ. Before the first await, `SynchronizationContext.Current` is Blazor's `RendererSynchronizationContext`. It is not a native UI thread, and the managed thread ID does not have to be `1`: the Pool thread column says `yes` for every checkpoint. Blazor Server has no dedicated UI thread: the renderer's synchronization context runs its work on thread pool threads, one work item at a time, and that context is what serializes access to component state. Checkpoint 2 shows it: the plain `await` posted its continuation back to the renderer's context, which ran it on a different thread from checkpoint 1.

After `ConfigureAwait(false)` on an operation that has to wait, the continuation runs on whichever thread pool thread completed the awaited operation, and `SynchronizationContext.Current` is `null`. In the run above, thread `18` ran checkpoints 2 and 3 on the renderer's context and then ran checkpoint 4 with no synchronization context at all. The thread is not what changed. `ConfigureAwait(false)` told the awaiter not to capture the context, so nothing restored it when the continuation was scheduled.

Checkpoint 3 is the non-null result after `ConfigureAwait(false)`. The cached story's `ValueTask` had already completed when the `await` ran, so there was no continuation to schedule, and the code kept running on the same thread with the same context. `ConfigureAwait(false)` only affects continuations that are actually scheduled.

Checkpoint 5 is the one that surprises people. It is a plain `await`, but it does not bring you back to the renderer, because an `await` captures whatever `SynchronizationContext` is current when that `await` runs. After checkpoint 4 there was none. The only way back is to ask for it: checkpoint 6 runs inside `InvokeAsync(...)`, which runs the lambda through Blazor's renderer, and reports `RendererSynchronizationContext`, even though it ran on the same thread as checkpoint 5.

The key observation is that `ConfigureAwait(false)` avoids capturing the synchronization context when a continuation is scheduled. After that, every change to component state goes back through `InvokeAsync(...)`, just as Correcting Common Async Await Mistakes does.
