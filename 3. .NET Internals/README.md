# .NET Internals

In this section, you will inspect the internal state that .NET uses to flow asynchronous data across threads.

**InternalsLab** is a lab notebook with four experiments: `[ThreadStatic]` fields, `ExecutionContext`, the signed-in user, and `SynchronizationContext`. Nothing in it is broken, and there is no Start or Finish folder. Your job is to predict what the code will see, run it, compare, and explain what happened in your own words.

## 1. Open the App

1. Using File Explorer on Windows or Finder on macOS, navigate to **BecomeAnExpertWithAsyncAwait/3. .NET Internals**.
2. Open **InternalsLab.slnx** in your IDE.
3. Run the project, ideally with your IDE's debugger attached, and open the app in your browser.

The app runs at [http://localhost:5003](http://localhost:5003).

The app opens on **Workshop steps**. Step 1 is unlocked, and every later step unlocks when the step before it passes.

## 2. Inspect the Code

1. Open the **InternalsLab/Experiments** folder. `ThreadStaticExperiment.cs` is Step 1, `ExecutionContextExperiment.cs` is Step 2, and `SynchronizationContextExperiment.cs` with `SimulatedStoryFeed.cs` is Step 4. Every `// Step N:` comment marks something to read before you predict, and every `// Try it:` comment suggests a change to explore once the step passes.
2. Open **InternalsLab/Controllers/PrincipalController.cs**. It is Step 3. The Principal experiment is an MVC controller, because it has to observe a real ASP.NET Core request. **InternalsLab/Controllers/AccountController.cs** and **InternalsLab/Program.cs** show how you get signed in.
3. Skim **InternalsLab/Notebook**, **InternalsLab/Components**, and **InternalsLab/Views**. They are the workshop plumbing that runs the experiments and draws the pages. You do not need to change them.
4. Leave **InternalsLab/Steps** for later. Those files hold the hints, the questions, and their answers.

The app walks you through the investigation one step at a time:

1. Step 1 is ThreadStatic, Step 2 is ExecutionContext, Step 3 is Principal, and Step 4 is SynchronizationContext.
2. A step unlocks only after the step before it passes. Each step page tells the story behind the experiment, names the file to read, and asks you to predict what every checkpoint will see.
3. **Run the experiment** stays disabled until you choose a prediction for every checkpoint, and your predictions lock in when it runs. They do not have to be right.
4. The results show what really happened next to what you predicted, with ✅ or ❌, and a hint about what to look at under every result that does not match.
5. Each step ends with explain questions. A wrong answer shows a hint, and you can choose again. A step passes when its experiment has run and every question is answered correctly.
6. The lab notebook keeps your predictions, results, and answers for as long as the app runs, so reloading a page is safe. Stopping the app starts a fresh notebook, so explore the `// Try it:` comments with Hot Reload, or after the group review.

## 3. Challenge: Investigate the Internals

Recommended time: 40 minutes in total. Step 1 takes 8 minutes, Step 2 takes 12, Step 3 takes 10, and Step 4 takes 10.

> **Note:** Please avoid letting AI Agents solve the challenges for you. You're smart. You got this. Use them to understand the existing code, clarify runtime concepts, interpret debugger observations, and ask questions that help you form your own explanation.

Work through the steps in order on the Workshop steps page. For each one, read the experiment code before you predict. If you want to see it happen, set a breakpoint on a checkpoint and inspect the threads in your debugger.

### Step 1: ThreadStatic

Recommended time: 8 minutes.

1. Open **InternalsLab/Experiments/ThreadStaticExperiment.cs**.
2. Before running the experiment, predict which values will be shared and which values will stay isolated to each thread, including after `await Task.Yield()`.
3. Run the experiment and compare the results with your predictions.
4. Explain why the main thread keeps its value, why each background thread has its own value, and what happens to the value after the `await`.

### Step 2: ExecutionContext

Recommended time: 12 minutes.

1. Open **InternalsLab/Experiments/ExecutionContextExperiment.cs**.
2. Before running the experiment, predict the culture, principal, and `AsyncLocal` value at each `RecordThreadValues(int)` call.
3. Run the experiment. If you want to step through it, set a breakpoint in `RunAsync()` before you click.
4. Explain what changes when `ExecutionContext.Run(...)` is used.
5. Explain what changes when `Task.Run(...)` flows `ExecutionContext` automatically.
6. Explain why the task created inside `using (ExecutionContext.SuppressFlow())` sees default values.
7. Explain why the task is awaited only after leaving the `using` block, then click **Try it** to see what happens when it is awaited inside the block.

### Step 3: Principal

Recommended time: 10 minutes.

The previous step showed `ExecutionContext` flowing through a console-style program. This step shows the same mechanism inside an ASP.NET Core request, where the ambient values are the signed-in user and the current `HttpContext`.

1. Open **InternalsLab/Controllers/PrincipalController.cs** and read `RunExperiment()`. Each `Observe(string)` call records a checkpoint: the current thread ID, plus the signed-in user's name as seen through four different expressions.
2. On the Step 3 page, predict every cell of this grid as your name or `null`:

    | Checkpoint | `Thread.CurrentPrincipal` | `httpContextAccessor.HttpContext?.User` | `HttpContext.User` | `signedInUser` |
    | --- | --- | --- | --- | --- |
    | 1. Start of the action | | | | |
    | 2. After `await Task.Yield()` | | | | |
    | 3. Inside `Task.Run(...)` | | | | |
    | 4. Inside `Task.Run(...)` started while `ExecutionContext` flow is suppressed | | | | |

3. Select **Sign in** and sign in with your first name. You come straight back to Step 3.
4. Select **Run the experiment**. The page leaves the Blazor app for a normal MVC request, runs the experiment, and brings you back to compare the four name columns with your predictions. Note which thread ran each checkpoint.
5. Select **Run it again** a few times. What changes from run to run, and what never changes?
6. For each column, explain why the value is or is not still there at checkpoints 2, 3, and 4. Which values did `ExecutionContext` carry, and which were simply object references the code already held?
7. Use checkpoint 1 to explain whether ASP.NET Core sets `Thread.CurrentPrincipal` for you. Open **InternalsLab/Program.cs** to see where `HttpContext.User` gets its value.

### Step 4: SynchronizationContext

Recommended time: 10 minutes.

1. Open **InternalsLab/Experiments/SynchronizationContextExperiment.cs**. `RefreshAsync(...)` is shaped like the `RefreshAsync` method you fixed in Correcting Common Async Await Mistakes, with a simulated story feed instead of Hacker News.
2. Before running the experiment, predict the `SynchronizationContext` at each `RecordSynchronizationContext(int)` call: before the first `await`, after each continuation, and inside `InvokeAsync(...)`.
3. Run the experiment. The Step 4 page calls `RefreshAsync(...)` straight from its click handler, so it starts on Blazor's renderer. Compare the results and the thread IDs with your predictions.
4. Explain why `ConfigureAwait(false)` avoids capturing Blazor's synchronization context when a continuation is scheduled, why synchronous completion may leave the current context unchanged, and why a plain `await` after that does not bring you back.

Acceptance checks:

1. **InternalsLab.slnx** builds.
2. The Workshop steps page shows 4 of 4 steps pass.
3. You can explain every checkpoint that did not match your prediction in your own words.

## 4. Review the Solution

After you have attempted the investigation challenges, pause here for group review.

We will compare observations, debug through the experiments together, and answer questions before opening [SOLUTION.md](SOLUTION.md) together.

The solution walkthrough gives the step-by-step path through each experiment and the observations you should be able to explain.
