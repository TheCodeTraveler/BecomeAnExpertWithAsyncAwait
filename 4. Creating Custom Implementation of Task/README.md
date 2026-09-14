# Creating Custom Implementation of Task

In this section, you will build a minimal custom awaitable named `CustomTask`.

## 1. Open the Starter Project

1. Using File Explorer on Windows or Finder on macOS, navigate to **BecomeAnExpertWithAsyncAwait/4. Creating Custom Implementation of Task/1. Start**.
2. Open **CreatingTaskFromScratch.slnx** in your IDE.
3. Run the project once and read the exception. The starter compiles, but every `CustomTask` member is still a stub that throws a `NotImplementedException`, so the program stops at the first one it calls.

## 2. Inspect the Starting Code

1. Open **CreatingTaskFromScratch/CustomTask.cs**.
2. Open **CreatingTaskFromScratch/CustomTaskAwaiter.cs**.
3. Open **CreatingTaskFromScratch/Program.cs**.
4. Notice that `CustomTaskAwaiter` and `Program.cs` are already complete, and that `CustomTask` already declares every public member they call. Each member throws a `NotImplementedException` whose message is a hint about what that member needs to do.

`CustomTaskAwaiter` is provided so you can concentrate on `CustomTask` itself. Read it before you start: its three members tell you which `CustomTask` members the `await` keyword depends on, and `CustomTask` still needs a `GetAwaiter()` method that returns it.

`Program.cs` is the finished program, and it is your guide. It calls every public member of `CustomTask` in five numbered steps. Its comments explain what each member should do, and every line it prints ends with the `Expected:` result, so you can check your work as you go.

You are going to add just enough infrastructure to understand how task-like types work with continuations, blocking waits, timers, `ExecutionContext`, and the `await` keyword. This challenge builds on the .NET Internals section: the `ExecutionContext` flow you observed there is the same context your `CustomTask` must capture and restore when it runs continuations.

## 3. Challenge: Build an Awaitable CustomTask

Recommended time: 45 minutes.

> **Note:** Please avoid letting AI Agents solve the challenges for you. You're smart. You got this. Use them to understand the existing code, clarify task-like type concepts, interpret errors, and ask questions that help you decide what to change. The goal is to practice the reasoning yourself.

Implement `CustomTask` so the `Program.cs` you were given can run, continue, wait on, delay, await, and manually complete your custom type.

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
13. Finish with `Program.cs` and `CustomTaskAwaiter.cs` unchanged. Temporary test code at the bottom of `Program.cs` while you work through the acceptance checks is expected; delete it before you compare with **2. Finish**.

Acceptance checks:

1. **CreatingTaskFromScratch.slnx** builds.
2. The program runs all five steps in `Program.cs`, and every line of output matches its `Expected:` result.
3. The continuation runs after the first task completes.
4. Chaining `ContinueWith(...)` on the task returned by `ContinueWith(...)` does not wait forever.
5. Two callers waiting on the same incomplete `CustomTask` both resume when it completes.
6. A chain of several thousand `ContinueWith(...)` calls completes without a stack overflow.
7. The final code is ready to compare with **2. Finish/CreatingTaskFromScratch**.

## 4. Review the Solution

After you have attempted the challenge, pause here for group review.

We will compare approaches, talk through continuation and completion tradeoffs, and answer questions before opening [SOLUTION.md](SOLUTION.md) together.

The solution walkthrough shows the order to add each piece and points you to the completed code in **2. Finish/CreatingTaskFromScratch**.
