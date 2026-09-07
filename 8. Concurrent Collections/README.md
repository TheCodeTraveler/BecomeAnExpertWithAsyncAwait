# Concurrent Collections

In this section, you will make the Blazor **StockWatch** dashboard safe for the 60 threads that write to it. StockWatch tracks 60 symbols, refreshes every one of them in parallel on a 2 second timer, and paints a card per symbol with the current price and the percent change since the previous close. It works, but it does not tell the truth: cards go missing, the header counter undercounts, and nothing in the build output warns you about any of it.

The **1. Start** folder contains the intentionally imperfect code you will edit. The **2. Finish** folder contains the completed Blazor version.

## 1. Open the Starter Project

1. Using File Explorer on Windows or Finder on macOS, navigate to **BecomeAnExpertWithAsyncAwait/8. Concurrent Collections/1. Start**.
2. Open **StockWatch.slnx** in your IDE.
3. Build the project once and notice that it builds with zero warnings.
4. Run the project and open the dashboard in your browser.
5. Watch it for a minute, and reload it a few times, before you change anything.

```console
dotnet build StockWatch.slnx
```

```console
dotnet run --project StockWatch/StockWatch.csproj
```

The dashboard is at [http://localhost:5005](http://localhost:5005).

> **Note:** This app has a race condition, not a guaranteed crash. Some runs look perfect. Reload the page several times before you decide the bug is not there. The page is also blank for a moment on every load while the Blazor circuit connects, which is normal. If the page goes blank and then stops updating entirely, check the `dotnet run` terminal: the parallel loop threw, and this project has no on-page error banner to tell you.

## 2. Inspect the Starting Code

1. Open **StockWatch/Components/Pages/Dashboard.razor.cs** and find each `// ToDo Refactor` comment. There are five.
2. Read `RefreshQuotes(CancellationToken)`. It calls `Parallel.ForEachAsync(...)` over all 60 symbols, so everything inside that lambda runs on many Thread Pool threads at the same time.
3. Read `GetSymbols()`. The `Symbols` property calls it on every render, and it uses `Parallel.ForEach(...)` to build the list the grid loops over.
4. Read `StartRefreshTimer()` and `StopRefreshTimer()`. Both read and write `_refreshTimer`, and nothing coordinates them.
5. Open **StockWatch/Services/MarketDataService.cs**. It is a simulated in-process feed that behaves like a remote quote API, so the sample runs offline with no API key, no rate limit, and the same starting prices for every attendee. Notice that `GetStockQuote(...)` waits somewhere under 60 milliseconds before it returns, exactly like a remote call would.
6. Open **StockWatch/Components/Pages/Dashboard.razor** and notice that the grid renders whatever `Symbols` hands it, that a symbol with no quote renders `--` instead of a price, and that the header tile renders `RefreshCount` as "quotes applied".

Now watch what the browser actually does:

1. Count the cards in the grid. There should be 60. You will usually count somewhere in the forties or fifties, and the number is different on every reload. Every so often the render throws instead: the torn `List<T>` leaves a null in its backing array, `OrderBy` dereferences it, and the circuit dies. You get no grid at all, and a `NullReferenceException` in the `dotnet run` terminal. That stack trace is expected in the starter project.
2. Look at the cards that did render. Some of them show `--` instead of a price. `GetSymbols()` builds a card for all 60 symbols either way, so a card showing `--` is a quote that never made it into `_latestQuotes`. A card that is missing entirely is an item that never made it into the `List`. Two different bugs, two different symptoms.
3. Look at the "quotes applied" tile. The first render should read 60, one per symbol. Every so often it reads 59 instead, more often on a machine with more cores. This is the quietest of the bugs: one lost increment out of sixty, with nothing to tell you it happened.
4. Wait 2 seconds. The tile should climb by exactly 60 on every tick. It usually does, and every so often it climbs by 59.
5. Reload the browser several times. You get a different card count almost every time. The same code, the same input, and a different answer on every run is the signature of a race condition.
6. Look back at the build output. Zero warnings. The compiler cannot see any of this.

Pay attention to these clues:

1. `_latestQuotes` is a `Dictionary<string, StockQuoteModel>`, and `Parallel.ForEachAsync(...)` writes to it from many Thread Pool threads at once. `Dictionary<TKey, TValue>` supports one writer at a time.
2. Each write is a `TryAdd(...)` followed by an indexer assignment. That is two separate operations on shared state, and another thread can slip in between them.
3. `_refreshCount++` looks like one operation. It is three: read the field, add one, write it back. Increments that interleave get lost.
4. `RefreshCount` is read by Blazor's renderer on a different thread than the one that last wrote `_refreshCount`.
5. `GetSymbols()` fills a `List<StockSymbolModel>` from inside `Parallel.ForEach(...)`. `List<T>` is not thread safe either, so items go missing or the call throws.
6. `StartRefreshTimer()` and `StopRefreshTimer()` both touch `_refreshTimer`, and nothing stops them running at the same time. A component being disposed mid-initialization can null the field out from under the method that is still assigning it.
7. The timer callback and the `Parallel.ForEachAsync(...)` body both run away from Blazor's renderer, which is why the refresh already ends with `InvokeAsync(StateHasChanged)`.

## 3. Challenge: Make the Dashboard Thread Safe

Recommended time: 25 minutes.

> **Note:** Please avoid letting AI Agents solve the challenges for you. You're smart. You got this. Use them to understand the existing code, clarify concurrent collection concepts, interpret errors, and ask questions that help you decide what to change. The goal is to practice the reasoning yourself.

Refactor **Dashboard.razor.cs** so every shared field survives 60 threads writing to it at once.

Requirements:

1. Add `using System.Collections.Concurrent;`.
2. Replace `_latestQuotes` with a collection from `System.Collections.Concurrent` that is built for many concurrent writers.
3. Replace the `TryAdd(...)` and indexer assignment with a single `AddOrUpdate(...)` call that keeps whichever quote has the newer `Timestamp`. It is one call rather than two, and it cannot lose an update, but it is not atomic, which is what the next requirement is about.
4. Keep the delegate you pass to that call cheap and free of side effects, because it runs outside the collection's internal lock and can be invoked more than once.
5. Replace `_refreshCount++` with an atomic increment.
6. Read `_refreshCount` in a way that forces a real read of the field instead of a value the JIT cached in a register.
7. Replace the `List<StockSymbolModel>` in `GetSymbols()` with a thread-safe collection many `Parallel.ForEach(...)` workers can add to, and keep the `OrderBy` so the cards stay alphabetical.
8. Add a `SemaphoreSlim` created with `new(1, 1)` and use it to guard every read and write of `_refreshTimer`. Do not use `lock`. `lock` cannot be held across an `await`, and every method here awaits.
9. Wait on the semaphore with `await WaitAsync(...)` and release it inside a `finally` block.
10. Move the timer disposal into a private method that assumes the semaphore is already held, and call that method from inside both guarded blocks. `SemaphoreSlim` is not reentrant, so a guarded method that calls another guarded method waits on itself forever.
11. Dispose the semaphore in `DisposeAsync()`.
12. Do not remove `InvokeAsync(StateHasChanged)` or the `CancellationToken` plumbing that is already there.

Acceptance checks:

1. **StockWatch.slnx** builds with no warnings.
2. Open [http://localhost:5005](http://localhost:5005) and count the cards. There are 60, on the first paint and after every refresh.
3. The "quotes applied" tile reads exactly 60 when the page first paints.
4. Two seconds later it reads 120, then 180, then 240. It climbs by exactly 60 on every tick.
5. Reload the browser eight times in a row and get 60 cards and a first render of 60 every single time.
6. Prices and percent changes keep updating, and no card is stuck showing `--`.
7. Leave the page open for a minute, then reload the browser. The prices keep updating after the reload, and the `dotnet run` terminal shows no exception.
8. Your code is ready to compare with **2. Finish/StockWatch**.

If you finish early, swap the collection you chose in `GetSymbols()` for a different type from `System.Collections.Concurrent` and confirm the dashboard still renders all 60 cards. Watch out: this is not always a pure type swap, because not every one of these collections exposes an `Add` method. Then work out why the `OrderBy` on the last line of `GetSymbols()` is what makes the two interchangeable here, and which of the two you would actually ship.

## 4. Review the Solution

After you have attempted the challenge, pause here for group review.

We will compare approaches, talk through which collection fits which job and what each one costs, and answer questions before opening [SOLUTION.md](SOLUTION.md) together.

The solution walkthrough shows the order to make each change and points you to the completed code in **2. Finish/StockWatch**.
