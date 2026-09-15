# .NET Internals

In this section, you will inspect the internal state that .NET uses to flow asynchronous data across threads.

## 1. ThreadStatic Challenge

Recommended time: 8 minutes.

> **Note:** Please avoid letting AI Agents solve the challenges for you. You're smart. You got this. Use them to understand the existing code, clarify runtime concepts, interpret debugger observations, and ask questions that help you form your own explanation.

1. Open **1. Thread Static/ThreadStaticExample.slnx**.
2. Open **ThreadStaticExample/Program.cs**.
3. Before running the app, predict which values will be shared and which values will stay isolated to each thread.
4. Build and run the project.
5. Write down why the main thread keeps its value and why each background thread has its own value.

## 2. ExecutionContext Challenge

Recommended time: 12 minutes.

> **Note:** Please avoid letting AI Agents solve the challenges for you. You're smart. You got this. Use them to understand the existing code, clarify runtime concepts, interpret debugger observations, and ask questions that help you form your own explanation.

1. Open **2. ExecutionContext/ExecutionContextExample.slnx**.
2. Open **ExecutionContextExample/Program.cs**.
3. Before running the app, predict the culture, principal, and `AsyncLocal` value at each `PrintThreadValues()` call.
4. Debug the project and step through each call.
5. Explain what changes when `ExecutionContext.Run(...)` is used.
6. Explain what changes when `Task.Run(...)` flows `ExecutionContext` automatically.
7. Explain why the task created inside `using (ExecutionContext.SuppressFlow())` sees default values.
8. Explain why the task is awaited only after leaving the `using` block.

## 3. Principal Challenge

Recommended time: 10 minutes.

> **Note:** Please avoid letting AI Agents solve the challenges for you. You're smart. You got this. Use them to understand the existing code, clarify runtime concepts, interpret debugger observations, and ask questions that help you form your own explanation.

The previous challenge showed `ExecutionContext` flowing through a console app. This challenge shows the same mechanism inside an ASP.NET Core request, where the ambient values are the signed-in user and the current `HttpContext`.

1. Open **3. Principal/PrincipalExample.slnx**.
2. Open **PrincipalExample/Controllers/HomeController.cs** and read `RunExperiment()`. Each `Observe(string)` call records a checkpoint: the current thread ID, plus the signed-in user's name as seen through four different expressions.
3. Before running the app, copy this grid into your notes. In each cell, predict whether that expression returns your name or `null` at that checkpoint:

    | Checkpoint | `Thread.CurrentPrincipal` | `httpContextAccessor.HttpContext?.User` | `HttpContext.User` | `signedInUser` |
    | --- | --- | --- | --- | --- |
    | 1. Start of the action | | | | |
    | 2. After `await Task.Yield()` | | | | |
    | 3. Inside `Task.Run(...)` | | | | |
    | 4. Inside `Task.Run(...)` started while `ExecutionContext` flow is suppressed | | | | |

4. Run or debug the project. If your IDE does not open a browser, navigate to [http://localhost:5000](http://localhost:5000). Sign in with your first name.
5. Select **Run the experiment**. Compare the four name columns with your predictions, and note which thread ran each checkpoint.
6. Select **Run it again** a few times. What changes from run to run, and what never changes?
7. For each column, explain why the value is or is not still there at checkpoints 2, 3, and 4. Which values did `ExecutionContext` carry, and which were simply object references the code already held?
8. Use checkpoint 1 to explain whether ASP.NET Core sets `Thread.CurrentPrincipal` for you. Open **PrincipalExample/Program.cs** to see where `HttpContext.User` gets its value.
9. Explain why the checkpoint 4 task is created inside the `using` block but awaited after it.

## 4. SynchronizationContext Challenge

Recommended time: 10 minutes.

> **Note:** Please avoid letting AI Agents solve the challenges for you. You're smart. You got this. Use them to understand the existing code, clarify runtime concepts, interpret debugger observations, and ask questions that help you form your own explanation.

1. Open **4. SynchronizationContext/HackerNews.slnx**.
2. Open **HackerNews/Components/Pages/News.razor.cs**.
3. Before running, predict the thread ID and `SynchronizationContext` that each `Logger.LogInformation(...)` call in `RefreshAsync(CancellationToken)` will report, both before `ConfigureAwait(false)` and after each continuation.
4. Run the app and open [http://localhost:5004](http://localhost:5004).
5. Read the `NewsPageBase` log lines and compare them with your predictions.
6. Explain why `ConfigureAwait(false)` avoids capturing Blazor's synchronization context when a continuation is scheduled, and why synchronous completion may leave the current context unchanged.

## 5. Review the Solution

After you have attempted the investigation challenges, pause here for group review.

We will compare observations, debug through the samples together, and answer questions before opening [SOLUTION.md](SOLUTION.md) together.

The solution walkthrough gives the step-by-step path through each sample and the observations you should be able to explain.
