# .NET Internals

In this section, you will inspect the internal state that .NET uses to flow asynchronous data across threads.

## 1. ThreadStatic Challenge

Recommended time: 15 to 20 minutes.

1. Open **1. Thread Static/ThreadStaticExample.slnx**.
1. Open **ThreadStaticExample/Program.cs**.
1. Before running the app, predict which values will be shared and which values will stay isolated to each thread.
1. Build and run the project.
1. Write down why the main thread keeps its value and why each background thread has its own value.

## 2. Principal Challenge

Recommended time: 25 to 35 minutes.

1. Open **2. Principal/PrincipalExample.slnx**.
1. Open **PrincipalExample/Controllers/AccountController.cs**.
1. Set breakpoints around the `SignInAsync(...)` await and the redirect that follows it.
1. Debug the app and navigate to [http://localhost:5000/Account/Login](http://localhost:5000/Account/Login).
1. Record the managed thread ID, `HttpContext`, and claims before and after the await.
1. Explain why the security context remains available after the continuation runs.

## 3. ExecutionContext Challenge

Recommended time: 30 to 45 minutes.

1. Open **3. ExecutionContext/ExecutionContextExample.slnx**.
1. Open **ExecutionContextExample/Program.cs**.
1. Before running the app, predict the culture, principal, and `AsyncLocal` value at each `PrintThreadValues()` call.
1. Debug the project and step through each call.
1. Explain what changes when `ExecutionContext.Run(...)` is used.
1. Explain what changes when `Task.Run(...)` flows `ExecutionContext` automatically.
1. Explain why the task created inside `using (ExecutionContext.SuppressFlow())` sees default values.
1. Explain why the task is awaited only after leaving the `using` block.

## 4. SynchronizationContext Challenge

Recommended time: 25 to 35 minutes.

1. Open **4. SynchronizationContext/HackerNews.slnx**.
1. Open **HackerNews/ViewModels/NewsViewModel.cs**.
1. Set breakpoints before and after `ConfigureAwait(false)` in the refresh flow.
1. Debug the app and trigger a refresh.
1. Inspect the current thread before the await and after the continuation.
1. Explain why the continuation after `ConfigureAwait(false)` is not running on the captured synchronization context.

## 5. Review the Solution

After you have attempted the investigation challenges, pause here for group review.

We will compare observations, debug through the samples together, and answer questions before opening [SOLUTION.md](SOLUTION.md) together.

The solution walkthrough gives the step-by-step debugger path and the observations you should be able to explain.
