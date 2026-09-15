# Correcting Common Async Await Mistakes

In this section, you will refactor the Blazor **HackerNews** starter app to fix common async/await mistakes.

The **1. Start** folder contains the intentionally imperfect code you will edit. The **2. Finish** folder contains the completed Blazor version.

## 1. Open the Starter Project

1. Using File Explorer on Windows or Finder on macOS, navigate to **BecomeAnExpertWithAsyncAwait/2. Correcting Common Async Await Mistakes/1. Start**.
2. Open **HackerNews.slnx** in your IDE.
3. Build the project once so you can see the starting warnings and confirm your environment is ready.
4. Run the app and open it in your browser.

The starter app runs at [http://localhost:5001](http://localhost:5001). The finished app runs at [http://localhost:5002](http://localhost:5002), so you can run both at the same time and compare them later.

The app opens on the **Top stories** page, with the **workshop guide** docked beside it. Every step in the guide is one async/await mistake in the Top stories page, and every time the app starts it checks your code against them. Right now the guide says it stopped at Step 1. Open that step to see every result that did not match, with a hint for each one.

The steps render your News page against an in-memory Hacker News, so they work offline and on networks that block Hacker News. The **Top stories** page itself still loads the real Hacker News API.

## 2. Inspect the Starting Code

1. Open **HackerNews/Components/Pages/News.razor.cs**.
2. Find each `// ToDo Refactor (Step N)` comment. The step number tells you which step checks that line.
3. Read `OnInitialized()`, `RefreshAsync(CancellationToken)`, `GetTopStories(...)`, `GetStory(...)`, and `GetTopStoryIDs(...)` before changing anything.
4. Notice which code runs during Blazor component initialization and which code updates component state.
5. Open the **HackerNews/Steps** folder. Each `Step*.cs` file renders your News page the way Blazor does for a browser tab, with comments that explain what each expected result checks and why. These files are your guide.
6. Skim **HackerNews/Verification** and **HackerNews/Components/Layout/WorkshopGuide.razor**. They are the workshop plumbing that renders your page against an in-memory Hacker News, runs the steps, and draws the guide. You do not need to change them.

Now reload **Top stories** and watch the count of loaded stories in the orange bar: it sits at 0 behind the loading skeleton, then jumps straight to 50. Press **Refresh**, then reload the browser tab before the refresh finishes. The page is gone, but its refresh keeps running on the server.

Pay attention to these clues:

1. A task is started from a lifecycle method without being awaited.
2. A cancellation token is accepted but not always forwarded.
3. A continuation is allowed to capture context even when it does not need to.
4. A blocking wait is used inside an async method.
5. Story loading waits for a full list before the UI can process results.
6. Some methods create async state machines even though they only wrap another async call.
7. UI state changes must be marshaled through Blazor's renderer when continuations run away from the captured context.

The app walks you through the challenge one step at a time:

1. Step 1 loads the stories as part of initialization, Step 2 keeps Blazor's renderer free, Step 3 stops refresh work when the page goes away, Step 4 streams stories as they arrive, and Step 5 skips the state machines you don't need.
2. A step unlocks only after the step before it passes. Select a step in the guide to read the story behind the mistake, how to see it on the Top stories page, which file to change, and a task list for that step. If you get stuck, it has clues.
3. Each step renders your real News page in memory against an in-memory Hacker News whose delays the step controls, and shows a checklist of every expected result next to what actually happened. Every result that does not match comes with a hint right under it.
4. Every time the app starts, it checks your code against every step, so the guide always reflects the code you have now.
5. Stop and run the app again after each change. If your IDE applied the change with Hot Reload, click **Run every step** at the top of the guide instead.

## 3. Challenge: Refactor the Refresh Flow

Recommended time: 35 minutes.

> **Note:** Please avoid letting AI Agents solve the challenges for you. You're smart. You got this. Use them to understand the existing code, clarify async/await concepts, interpret errors, and ask questions that help you decide what to change. The goal is to practice the reasoning yourself.

Refactor **News.razor.cs** so the refresh pipeline follows the async/await and Blazor practices covered in this workshop. Leave **News.razor**, the **Services** folder, and **Program.cs** alone.

Requirements, in the order the steps check them:

1. Do not leave an unobserved task in component initialization.
2. Avoid `async void` unless the framework API truly requires it.
3. Use safe fire-and-forget only when the caller cannot return `Task`.
4. Replace blocking waits with `await`.
5. Use `ConfigureAwait(false)` only where the continuation does not need the captured context.
6. Marshal UI updates through `InvokeAsync(...)` when the continuation may not be on Blazor's renderer context.
7. Forward the supplied `CancellationToken` to cancellable async APIs.
8. Log full exceptions server-side and show users a generic actionable refresh error.
9. Stream stories with `IAsyncEnumerable<StoryModel>`.
10. Return `Task` directly from methods that only wrap another task.
11. Use `ValueTask` only where the hot path can complete synchronously.

Acceptance checks:

1. **HackerNews.slnx** builds.
2. The workshop guide shows 5 of 5 steps pass after the app starts.
3. The Top stories page can refresh stories in the browser, and they appear as they arrive.
4. The refresh indicator stops after the minimum refresh delay.
5. Navigating away or disposing the component does not keep unnecessary refresh work alive.
6. Your code is ready to compare with **2. Finish/HackerNews**.

## 4. Review the Solution

After you have attempted the challenge, pause here for group review.

We will compare approaches, discuss the tradeoffs behind each refactor, and answer questions before opening [SOLUTION.md](SOLUTION.md) together.

The solution walkthrough shows the step-by-step refactor path and points you to the completed code in **2. Finish/HackerNews**.
