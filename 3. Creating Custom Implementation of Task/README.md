# Creating Custom Implementation of Task

In this section, you will build a minimal custom awaitable named `CustomTask`.

## 1. Open the Starter Project

1. Using File Explorer on Windows or Finder on macOS, navigate to **BecomeAnExpertWithAsyncAwait/3. Creating Custom Implementation of Task/1. Start**.
2. Open **CreatingTaskFromScratch.slnx** in your IDE.
3. Build the project once so you can confirm the starter compiles.

```console
dotnet build CreatingTaskFromScratch.slnx
```

## 2. Inspect the Starting Code

1. Open **CreatingTaskFromScratch/CustomTask.cs**.
2. Open **CreatingTaskFromScratch/Program.cs**.
3. Notice that `CustomTask` starts empty and `Program.cs` only prints the starting thread ID.

You are going to add just enough infrastructure to understand how task-like types work with continuations, blocking waits, timers, `ExecutionContext`, and the `await` keyword.

## 3. Challenge: Build an Awaitable CustomTask

Recommended time: 45 minutes.

Implement `CustomTask` and update `Program.cs` so your custom type can be run, continued, waited, delayed, and awaited.

Requirements:

1. Track completion state safely across threads.
2. Store exceptions and rethrow them without losing the original stack trace.
3. Implement `Run(Action)` using the thread pool.
4. Implement `ContinueWith(Action)` and preserve the caller's `ExecutionContext`.
5. Implement `Wait()` with a blocking wait primitive.
6. Implement `Delay(TimeSpan)` using `Timer`.
7. Add a `CustomTaskAwaiter` that enables the `await` keyword.
8. Update `Program.cs` so the final version uses `await` instead of `Wait()`.

Acceptance checks:

1. **CreatingTaskFromScratch.slnx** builds.
2. The program prints the starting thread ID and three `CustomTask` thread IDs.
3. The continuation runs after the first task completes.
4. The final code is ready to compare with **2. Finish/CreatingTaskFromScratch**.

## 4. Review the Solution

After you have attempted the challenge, pause here for group review.

We will compare approaches, talk through continuation and completion tradeoffs, and answer questions before opening [SOLUTION.md](SOLUTION.md) together.

The solution walkthrough shows the order to add each piece and points you to the completed code in **2. Finish/CreatingTaskFromScratch**.
