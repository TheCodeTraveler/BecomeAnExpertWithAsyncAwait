# Concurrent Collections

In this section, you will make the Blazor **StockWatch** dashboard safe for the concurrent writes that land on it. StockWatch tracks 60 symbols, refreshes every one of them in parallel on a 2 second timer, and paints a card per symbol with the current price and the percent change since the previous close. It works, but it does not tell the truth: cards go missing, the header counter undercounts, and nothing in the build output warns you about any of it.

The **1. Start** folder contains the intentionally imperfect code you will edit. The **2. Finish** folder contains the completed Blazor version.

## 1. Open the Starter Project

1. Using File Explorer on Windows or Finder on macOS, navigate to **BecomeAnExpertWithAsyncAwait/8. Concurrent Collections/1. Start**.
2. Open **StockWatch.slnx** in your IDE.
3. Build the project once and notice that it builds with zero warnings.
4. Run the project and open it in your browser.
5. Watch the **Dashboard** for a minute, and reload it a few times, before you change anything.

The starter app runs at [http://localhost:5005](http://localhost:5005). The finished app runs at [http://localhost:5006](http://localhost:5006), so you can run both at the same time and compare them later.

The app opens on the **Dashboard**, with the **workshop guide** docked beside it. Every step in the guide is one bug in the dashboard's code-behind, and every time the app starts it checks your code against them. Right now the guide says it stopped at Step 1, and the terminal running the app shows the same result, with a hint.

> **Note:** This app has a race condition, not a guaranteed crash. Some runs look perfect. Reload the page several times before you decide the bug is not there. The page is also blank for a moment on every load while the Blazor circuit connects, which is normal. If a **Feed fault** panel replaces the dashboard, check the `dotnet run` terminal: a collection that many threads touched at once threw while the dashboard loaded or rendered. Reload the browser tab to bring the dashboard back.

## 2. Inspect the Starting Code

1. Open **StockWatch/Components/Pages/Dashboard.razor.cs** and find each `// ToDo Refactor (Step N)` comment. There are eight, and the step number tells you which step checks that line.
2. Read `RefreshQuotes(CancellationToken)`. It calls `Parallel.ForEachAsync(...)` over all 60 symbols, so more than one of those refreshes can be in flight at once.
3. Read `GetSymbols()`. The `Symbols` property calls it on every render, and it uses `Parallel.ForEach(...)` to build the list the grid loops over.
4. Read `StartRefreshTimer()` and `StopRefreshTimer()`. Both read and write `_refreshTimer`, and nothing coordinates them.
5. Open **StockWatch/Services/MarketDataService.cs**. It is a simulated in-process feed that behaves like a remote quote API, so the sample runs offline with no API key, no rate limit, and the same starting prices for every attendee. Notice that `GetStockQuote(...)` waits somewhere under 60 milliseconds before it returns, exactly like a remote call would.
6. Open **StockWatch/Components/Pages/Dashboard.razor** and notice that the ticker tape and the grid both render whatever `Symbols` hands them, that a symbol with no quote renders `--` instead of a price, and that the header tile renders `RefreshCount` as "quotes applied".
7. Open the **StockWatch/Steps** folder. Each `Step*.cs` file renders a fresh dashboard with its own simulated feed and drives your code from many threads at once, with comments that explain what each expected result checks and why. These files are your guide.
8. Skim **StockWatch/Verification** and **StockWatch/Components/Layout/WorkshopGuide.razor**. They are the workshop plumbing that runs the steps and draws the guide. You do not need to change them.

Now watch what the **Dashboard** beside the guide actually does:

1. Count the cards in the grid. There should be 60. You will usually count somewhere in the forties or fifties, and the number is different on every reload. Every so often the render throws instead: the torn `List<T>` leaves a null in its backing array, and `OrderBy` dereferences it. A **Feed fault** panel replaces the dashboard until you reload, and the `dotnet run` terminal shows a `NullReferenceException`. That stack trace is expected in the starter project.
2. Look at the cards that did render. Some of them show `--` instead of a price. `GetSymbols()` builds a card for all 60 symbols either way, so a card showing `--` is a quote that never made it into `_latestQuotes`. A card that is missing entirely is an item that never made it into the `List`. Two different bugs, two different symptoms.
3. Look at the "quotes applied" tile. The first render should read 60, one per symbol. Every so often it reads 59 instead, more often on a machine with more cores. This is the quietest of the bugs: one lost increment out of sixty, with nothing to tell you it happened.
4. Wait 2 seconds. The tile should climb by exactly 60 on every tick. It usually does, and every so often it climbs by 59.
5. Reload the browser several times. You get a different card count almost every time. The same code, the same input, and a different answer on every run is the signature of a race condition.
6. Look back at the build output. Zero warnings. The compiler cannot see any of this.

Pay attention to these clues:

1. `_latestQuotes` is a `Dictionary<string, StockQuoteModel>`, and `Parallel.ForEachAsync(...)` refreshes all 60 symbols concurrently, so more than one write can be in flight at once. `Dictionary<TKey, TValue>` supports one writer at a time.
2. Each write is a `TryAdd(...)` followed by an indexer assignment. That is two separate operations on shared state, and another thread can slip in between them.
3. `_refreshCount++` looks like one operation. It is three: read the field, add one, write it back. Increments that interleave get lost.
4. `RefreshCount` is read by Blazor's renderer on a different thread than the one that last wrote `_refreshCount`.
5. `GetSymbols()` fills a `List<StockSymbolModel>` from inside `Parallel.ForEach(...)`. `List<T>` is not thread safe either, so items go missing or the call throws.
6. `StartRefreshTimer()` and `StopRefreshTimer()` both touch `_refreshTimer`, and nothing stops them running at the same time. A component being disposed mid-initialization can null the field out from under the method that is still assigning it.
7. The timer callback and the `Parallel.ForEachAsync(...)` body both run away from Blazor's renderer, which is why the refresh already ends with `InvokeAsync(StateHasChanged)`.

The app walks you through the challenge one step at a time:

1. Step 1 shows every card, Step 2 keeps every quote and keeps the newest, Step 3 counts every quote, and Step 4 guards the refresh timer.
2. A step unlocks only after the step before it passes. Select a step in the guide to read the story behind the bug, how to see it on the Dashboard, which lines to change, and a task list for that step. If you get stuck, it has clues.
3. Each step renders a fresh dashboard, drives your code from many threads at once, and shows a checklist of every expected result next to what actually happened. Every result that does not match comes with a hint, and the same hint is printed in the terminal running the app.
4. Every time the app starts, it checks your code against every step, so the guide always reflects the code you have now.
5. Stop and run the app again after each change. If your IDE applied the change with Hot Reload, click **Run every step** at the top of the guide instead.

## 3. Challenge: Make the Dashboard Thread Safe

Recommended time: 25 minutes.

> **Note:** Please avoid letting AI Agents solve the challenges for you. You're smart. You got this. Use them to understand the existing code, clarify concurrent collection concepts, interpret errors, and ask questions that help you decide what to change. The goal is to practice the reasoning yourself.

Refactor **Dashboard.razor.cs** so every shared field survives concurrent writes from all 60 symbol refreshes.

Requirements, in the order the steps check them:

1. Add `using System.Collections.Concurrent;`.
2. Replace the `List<StockSymbolModel>` in `GetSymbols()` with a thread-safe collection many `Parallel.ForEach(...)` workers can add to, and keep the `OrderBy` so the cards stay alphabetical.
3. Replace `_latestQuotes` with a collection from `System.Collections.Concurrent` that is built for many concurrent writers.
4. Replace the `TryAdd(...)` and indexer assignment with a single `AddOrUpdate(...)` call that keeps whichever quote has the newer `Timestamp`. It is one call rather than two, and it cannot lose an update, but it is not atomic, which is what the next requirement is about.
5. Keep the delegate you pass to that call cheap and free of side effects, because it runs outside the collection's internal lock and can be invoked more than once.
6. Replace `_refreshCount++` with an atomic increment.
7. Read `_refreshCount` in a way that forces a real read of the field instead of a value the JIT cached in a register.
8. Add a `SemaphoreSlim` created with `new(1, 1)` and use it to guard every read and write of `_refreshTimer`. Do not use `lock`. `lock` cannot be held across an `await`, and every method here awaits.
9. Wait on the semaphore with `await WaitAsync(...)` and release it inside a `finally` block.
10. Move the timer disposal into a private method that assumes the semaphore is already held, and call that method from inside both guarded blocks. `SemaphoreSlim` is not reentrant, so a guarded method that calls another guarded method waits on itself forever.
11. Dispose the semaphore in `DisposeAsync()`.
12. Do not remove `InvokeAsync(StateHasChanged)` or the `CancellationToken` plumbing that is already there.

Acceptance checks:

1. **StockWatch.slnx** builds with no warnings.
2. The workshop guide shows 4 of 4 steps pass after the app starts.
3. Open [http://localhost:5005](http://localhost:5005) and count the cards on the Dashboard. There are 60, on the first paint and after every refresh.
4. The "quotes applied" tile reads exactly 60 when the page first paints.
5. Two seconds later it reads 120, then 180, then 240. It climbs by exactly 60 on every tick.
6. Reload the browser eight times in a row and get 60 cards and a first render of 60 every single time.
7. Prices and percent changes keep updating, and no card is stuck showing `--`.
8. Leave the page open for a minute, then reload the browser. The prices keep updating after the reload, and the `dotnet run` terminal shows no exception.
9. Your code is ready to compare with **2. Finish/StockWatch**.

If you finish early, swap the collection you chose in `GetSymbols()` for a different type from `System.Collections.Concurrent` and confirm Step 1 still passes and the dashboard still renders all 60 cards. Watch out: this is not always a pure type swap, because not every one of these collections exposes an `Add` method. Then work out why the `OrderBy` on the last line of `GetSymbols()` is what makes the two interchangeable here, and which of the two you would actually ship.

## 4. Review the Solution

After you have attempted the challenge, pause here for group review.

We will compare approaches, talk through which collection fits which job and what each one costs, and answer questions before opening [SOLUTION.md](SOLUTION.md) together.

The solution walkthrough shows the order to make each change and points you to the completed code in **2. Finish/StockWatch**.
