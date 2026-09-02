# Correcting Common Async Await Mistakes

In this section, you will refactor the Blazor **HackerNews** starter app to fix common async/await mistakes.

The **1. Start** folder contains the intentionally imperfect code you will edit. The **2. Finish** folder contains the completed Blazor version.

## 1. Open the Starter Project

1. Using File Explorer on Windows or Finder on macOS, navigate to **BecomeAnExpertWithAsyncAwait/2. Correcting Common Async Await Mistakes/1. Start**.
2. Open **HackerNews.slnx** in your IDE.
3. Build the project once so you can see the starting warnings and confirm your environment is ready.
4. Open **HackerNews/Components/Pages/News.razor.cs**.

```console
dotnet build HackerNews.slnx
```

## 2. Inspect the Starting Code

1. Find each `// ToDo Refactor` comment.
2. Read `OnInitialized()`, `RefreshAsync(CancellationToken)`, `GetTopStories(...)`, `GetStory(...)`, and `GetTopStoryIDs(...)` before changing anything.
3. Notice which code runs during Blazor component initialization and which code updates component state.

Pay attention to these clues:

1. A task is started from a lifecycle method without being awaited.
2. A cancellation token is accepted but not always forwarded.
3. A continuation is allowed to capture context even when it does not need to.
4. A blocking wait is used inside an async method.
5. Story loading waits for a full list before the UI can process results.
6. Some methods create async state machines even though they only wrap another async call.
7. UI state changes must be marshaled through Blazor's renderer when continuations run away from the captured context.

## 3. Challenge: Refactor the Refresh Flow

Recommended time: 35 to 45 minutes.

> **Note:** Please avoid letting AI Agents solve the challenges for you. You're smart. You got this. Use them to understand the existing code, clarify async/await concepts, interpret errors, and ask questions that help you decide what to change. The goal is to practice the reasoning yourself.

Refactor **News.razor.cs** so the refresh pipeline follows the async/await and Blazor practices covered in this workshop.

Requirements:

1. Do not leave an unobserved task in component initialization.
2. Avoid `async void` unless the framework API truly requires it.
3. Use safe fire-and-forget only when the caller cannot return `Task`.
4. Forward the supplied `CancellationToken` to cancellable async APIs.
5. Use `ConfigureAwait(false)` only where the continuation does not need the captured context.
6. Marshal UI updates through `InvokeAsync(...)` when the continuation may not be on Blazor's renderer context.
7. Replace blocking waits with `await`.
8. Stream stories with `IAsyncEnumerable<StoryModel>`.
9. Return `Task` directly from methods that only wrap another task.
10. Use `ValueTask` only where the hot path can complete synchronously.
11. Log full exceptions server-side and show users a generic actionable refresh error.

Acceptance checks:

1. **HackerNews.slnx** builds.
2. The app can refresh stories in the browser.
3. The refresh indicator stops after the minimum refresh delay.
4. Navigating away or disposing the component does not keep unnecessary refresh work alive.
5. Your code is ready to compare with **2. Finish/HackerNews**.

## 4. Review the Solution

After you have attempted the challenge, pause here for group review.

We will compare approaches, discuss the tradeoffs behind each refactor, and answer questions before opening [SOLUTION.md](SOLUTION.md) together.

The solution walkthrough shows the step-by-step refactor path and points you to the completed code in **2. Finish/HackerNews**.
