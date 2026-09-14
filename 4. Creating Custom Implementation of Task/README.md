# Creating Custom Implementation of Task

In this section, you will build a minimal custom awaitable named `CustomTask`.

CoffeeShop's point-of-sale app runs on `CustomTask`, a home-grown `Task`, and nobody finished writing it. Every station in the shop is a real feature built on it: printing a receipt, brewing a pour-over, preparing a mobile order, wrapping the espresso machine's events, handling a jammed machine, and surviving rush hour. Your job is to finish `CustomTask.cs` so every station works.

The **1. Start** folder contains the starter app. The **2. Finish** folder contains the completed app.

## 1. Open the Starter Project

1. Using File Explorer on Windows or Finder on macOS, navigate to **BecomeAnExpertWithAsyncAwait/4. Creating Custom Implementation of Task/1. Start**.
2. Open **CoffeeShop.slnx** in your IDE.
3. Run the project and open the app in your browser.

The starter app runs at [http://localhost:5015](http://localhost:5015). The finished app runs at [http://localhost:5016](http://localhost:5016), so you can run both at the same time and compare them later.

The starter builds, but every `CustomTask` member is still a stub that throws a `NotImplementedException`. The Home page shows Step 1 as not implemented and names the member it hit first. The same exception, including the stub's hint, is in the terminal running the app.

## 2. Inspect the Starting Code

1. Open **CoffeeShop/CustomTask.cs**. Every public member is a stub, and each stub's message is a hint about what that member needs to do.
2. Open **CoffeeShop/CustomTaskAwaiter.cs**. It is already complete.
3. Open the **CoffeeShop/Steps** folder. Each `Step*.cs` file is one coffee shop feature built on `CustomTask`, with comments that explain what should happen and why, and an expected result for everything it checks. These files are your guide.
4. Skim **CoffeeShop/Components** and **CoffeeShop/Verification**. They are the workshop plumbing that runs the steps and draws the pages. You do not need to change them.

`CustomTaskAwaiter` is provided so you can concentrate on `CustomTask` itself. Read it before you start: its three members tell you which `CustomTask` members the `await` keyword depends on, and `CustomTask` still needs a `GetAwaiter()` method that returns it.

The app enforces the order of the steps:

1. Step 1 is `CustomTask.Run()`, `Wait()` and `IsCompleted`. Step 2 is `CustomTask.Delay()` and `ContinueWith()`. Step 3 is `await`, which uses `GetAwaiter()`. Step 4 is `SetResult()`. Step 5 is `SetException()`. Step 6, rush hour, covers many waiters, long `ContinueWith` chains and `ExecutionContext`.
2. A step unlocks only after the step before it passes. Each step page tells the story behind the feature, names the real .NET API it mirrors, and shows a live log plus a checklist of every expected result next to what actually happened.
3. Every time the app starts, it checks your `CustomTask` against Steps 1 to 5 automatically, so the Home page always reflects the code you have now. Step 6 runs only when you click **Run step** on its page, because an implementation that runs continuations inline overflows the stack there, and a stack overflow ends the whole process.
4. Stop and run the app again after each change to **CustomTask.cs**. If your IDE applied the change with Hot Reload, click **Run Steps 1-5** on the Home page instead.
5. Steps 4 and 5 drive a simulated espresso machine. Leave **Automatic barista** on, or turn it off and press the machine's button yourself to watch `IsCompleted` stay `False` until you do.

If the app exits, or the browser says it is reconnecting, a `CustomTask` member threw on a thread pool thread. An unhandled exception on a thread pool thread ends the whole process, and the terminal shows which stub it hit. That is exactly why `Run()` has to catch the exception its action throws and store it instead.

You are going to add just enough infrastructure to understand how task-like types work with continuations, blocking waits, timers, `ExecutionContext`, and the `await` keyword. This challenge builds on the .NET Internals section: the `ExecutionContext` flow you observed there is the same context your `CustomTask` must capture and restore when it runs continuations.

## 3. Challenge: Build an Awaitable CustomTask

Recommended time: 45 minutes.

> **Note:** Please avoid letting AI Agents solve the challenges for you. You're smart. You got this. Use them to understand the existing code, clarify task-like type concepts, interpret errors, and ask questions that help you decide what to change. The goal is to practice the reasoning yourself.

Implement `CustomTask` so every CoffeeShop station can run, continue, wait on, delay, await, and manually complete your custom type.

Requirements:

1. Track completion state safely across threads.
2. Store exceptions and rethrow them without losing the original stack trace.
3. Implement `public static CustomTask Run(Action action)` using the thread pool.
4. Implement `public CustomTask ContinueWith(Action action)` and complete the returned `CustomTask` when the continuation succeeds or fails.
5. Support multiple pending continuations on the same `CustomTask` and run all of them when it completes.
6. Preserve each caller's `ExecutionContext` when a continuation is registered before the antecedent completes, and never leak the completing thread's context into any continuation.
7. Queue continuations instead of invoking them inline so a long `ContinueWith` chain cannot overflow the stack.
8. Implement `public void Wait()` with a blocking wait primitive.
9. Implement `public static CustomTask Delay(TimeSpan delay)` using `Timer`, keep the timer alive until it fires, and dispose it from the callback.
10. Implement `public CustomTaskAwaiter GetAwaiter()` so the provided `CustomTaskAwaiter` enables the `await` keyword.
11. Implement `public void SetResult()` and `public void SetException(Exception exception)`, and complete each `CustomTask` exactly once.
12. Replace every `NotImplementedException` stub in `CustomTask.cs`.
13. Finish with every file except **CustomTask.cs** unchanged.

Acceptance checks:

1. **CoffeeShop.slnx** builds.
2. Steps 1 to 5 show as passed on the Home page after the app starts.
3. Step 6 passes when you run it from its page.
4. The final code is ready to compare with **2. Finish/CoffeeShop**.

## 4. Review the Solution

After you have attempted the challenge, pause here for group review.

We will compare approaches, talk through continuation and completion tradeoffs, and answer questions before opening [SOLUTION.md](SOLUTION.md) together.

The solution walkthrough shows the order to add each piece and points you to the completed code in **2. Finish/CoffeeShop**.
