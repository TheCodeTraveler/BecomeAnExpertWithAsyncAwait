# Solution Walkthrough: Concurrent Collections

Use this during the guided walkthrough after the challenge and group review in [README.md](README.md).

The completed project is **2. Finish/StockWatch**. Compare your decisions with the finished Blazor sample as we rebuild the solution step by step.

Every change lives in one file: **Components/Pages/Dashboard.razor.cs**. Nothing in `MarketDataService`, the models, or the markup needs to move.

## 1. Replace the Dictionary

`Dictionary<TKey, TValue>` is fast because it assumes it will never be written by two threads at once. `Parallel.ForEachAsync(...)` hands it 60 writers. A concurrent resize can lose entries, corrupt a bucket chain, or throw.

`ConcurrentDictionary<TKey, TValue>` is the replacement. Reads are lock-free, and writes take a lock from a small striped lock array, one lock per processor by default and capped at 1024, rather than one lock over the whole collection, so threads writing different keys usually do not wait on each other.

Add the namespace:

```cs
using System.Collections.Concurrent;
```

Then change the field:

```cs
    // ConcurrentDictionary is safe for many writers at once. AddOrUpdate is the
    // atomic read-modify-write that replaces TryAdd followed by an indexer assignment.
    readonly ConcurrentDictionary<string, StockQuoteModel> _latestQuotes = new();
```

## 2. Make Each Update a Single Call

The starter code calls `TryAdd(...)`, and when that returns `false` it assigns through the indexer. Those are two operations. Between them another thread can write a newer quote that your indexer assignment then overwrites. Swapping in a concurrent dictionary alone does not fix this, because each call being thread safe does not make the pair of them atomic.

`AddOrUpdate(...)` is one call instead of two, and it cannot lose an update, but it does not achieve that by holding a lock across the whole operation. It reads the current value, calls your delegate outside the lock, then takes the lock and installs the new value only if the stored value has not changed in the meantime. If it has, the result is discarded and the whole thing retries. That is why no update is lost, and why the delegate can run more than once.

The counter has the same shape of bug: `_refreshCount++` is a read, an add, and a write, and `Interlocked.Increment(...)` collapses those three steps into one hardware operation.

```cs
    async Task RefreshQuotes(CancellationToken token)
    {
        try
        {
            // Every symbol is fetched in parallel, so every write below
            // happens on a different Thread Pool thread at the same time.
            await Parallel.ForEachAsync(
                MarketDataService.Symbols,
                token,
                async (symbol, cancellationToken) =>
                {
                    var quote = await MarketDataService.GetStockQuote(symbol, cancellationToken).ConfigureAwait(false);

                    // Atomic: keep whichever quote is newer
                    _latestQuotes.AddOrUpdate(
                        symbol,
                        quote,
                        (_, existing) => quote.Timestamp > existing.Timestamp ? quote : existing);

                    // Atomic increment, safe from every thread
                    Interlocked.Increment(ref _refreshCount);
                }).ConfigureAwait(false);

            // The continuation is off Blazor's renderer, so marshal the UI update back
            await InvokeAsync(StateHasChanged).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Expected when the component is disposed or a refresh times out
        }
    }
```

The update delegate deserves a warning. Every method on `ConcurrentDictionary<TKey, TValue>` is thread safe, but `GetOrAdd(...)` and `AddOrUpdate(...)` are not atomic in the delegate they call. The delegate is invoked outside the dictionary's internal lock so unknown user code cannot block every thread touching that stripe of buckets, which means under contention it can run more than once. Keep it cheap and free of side effects. Comparing two timestamps and returning the winner can run twice with no harm. Writing to a database in there cannot.

The timestamp comparison matters for a second reason. When one 2 second tick runs long, two refreshes overlap, and without the comparison a slow thread carrying an older quote could overwrite a newer one that already landed.

## 3. Read the Counter Safely

`Interlocked.Increment(...)` makes the writes atomic and publishes them. What it does not do is stop the JIT from hoisting a plain `_refreshCount` read into a register and never looking at the field again.

```cs
    public int RefreshCount => Volatile.Read(ref _refreshCount);
```

`Volatile.Read(...)` forces a real acquire-ordered read of the field. In this component the `InvokeAsync(StateHasChanged)` hand-off already orders the render behind the writes, so this is defense in depth, but it costs nothing, and it tells the next person reading this property that the field is shared.

## 4. Collect Results in a ConcurrentBag

`GetSymbols()` runs `Parallel.ForEach(...)` and adds one item per symbol. `List<T>.Add` writes into an internal array and sometimes grows it, so parallel calls silently lose items, leave a null in the backing array for `OrderBy` to trip over, or throw. This is the failure the dashboard shows most often: a card is simply not there.

`ConcurrentBag<T>` is built for this shape of work. It is unordered, it allows duplicates, and it gives each producing thread its own local storage so adds rarely contend at all.

```cs
    IReadOnlyList<StockSymbolModel> GetSymbols()
    {
        // ConcurrentBag collects results from parallel workers without a lock
        ConcurrentBag<StockSymbolModel> symbols = [];

        Parallel.ForEach(MarketDataService.Symbols, symbol =>
        {
            _latestQuotes.TryGetValue(symbol, out var quote);

            symbols.Add(new StockSymbolModel(symbol, MarketDataService.GetCompanyName(symbol), quote));
        });

        return [.. symbols.OrderBy(static symbol => symbol.Symbol)];
    }
```

Unordered is fine here because the last line sorts the results anyway. If you needed indexing or `List<T>` semantics, a bag would be the wrong answer.

`List<T>` has no drop-in concurrent replacement. Pick by how you read the data back: keyed lookup is `ConcurrentDictionary<TKey, TValue>`, first in first out is `ConcurrentQueue<T>`, last in first out is `ConcurrentStack<T>`, and "just collect it and sort later" is `ConcurrentBag<T>`.

One honest footnote, because someone always asks. `Symbols` is evaluated on Blazor's renderer on every render, and `Parallel.ForEach(...)` is a blocking call that holds that thread until the last partition finishes. Sixty dictionary lookups do not need sixty-way parallelism, and the partitioning costs more than the work. In real code this method would be a plain LINQ projection over `MarketDataService.Symbols`, with no concurrent collection needed at all, because a body that shares nothing cannot race. The parallel loop is here because it is the clearest place to watch `ConcurrentBag<T>` do its job.

## 5. Guard the Timer With SemaphoreSlim

Nothing in the starter code orders `StartRefreshTimer()` against `StopRefreshTimer()`, so a component torn down mid-initialization can leave a disposed timer assigned or a live timer leaked. This is not a collection problem, so no concurrent collection can solve it.

You cannot use `lock` here, because the guarded work contains an `await` and a lock cannot be held across an `await`. `SemaphoreSlim` with a count of one is the asynchronous lock:

```cs
    // SemaphoreSlim is the asynchronous lock guarding the timer field.
    // `lock` cannot be held across an await, and DisposeAsync is awaited.
    readonly SemaphoreSlim _timerSemaphore = new(1, 1);
```

Wait on it before touching the field, and release it in a `finally` so a throw inside the guarded body cannot strand every later caller:

```cs
    async ValueTask StartRefreshTimer()
    {
        await _timerSemaphore.WaitAsync(_disposeCancellationTokenSource.Token).ConfigureAwait(false);

        try
        {
            // Call the unguarded version: SemaphoreSlim is not reentrant, so
            // calling StopRefreshTimer() here would deadlock against this same semaphore.
            await DisposeRefreshTimer().ConfigureAwait(false);

            _refreshTimer = new Timer(async _ =>
            {
                using var refreshCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellationTokenSource.Token);
                refreshCancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

                await RefreshQuotes(refreshCancellationTokenSource.Token).ConfigureAwait(false);
            });

            _refreshTimer.Change(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        }
        finally
        {
            _timerSemaphore.Release();
        }
    }
```

## 6. Split Out the Unguarded Core

`SemaphoreSlim` is not reentrant. The thread that holds it gets no special treatment, so a guarded method that calls another guarded method waits on a permit it is already holding and never returns.

That is why `StartRefreshTimer()` above does not call `StopRefreshTimer()`. The disposal moves into its own unguarded helper, and both public methods call it while they already hold the semaphore:

```cs
    async ValueTask StopRefreshTimer()
    {
        await _timerSemaphore.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            await DisposeRefreshTimer().ConfigureAwait(false);
        }
        finally
        {
            _timerSemaphore.Release();
        }
    }

    // Must only be called while the semaphore is already held
    async ValueTask DisposeRefreshTimer()
    {
        if (_refreshTimer is not null)
        {
            await _refreshTimer.DisposeAsync().ConfigureAwait(false);
            _refreshTimer = null;
        }
    }
```

Notice the two different tokens. `StartRefreshTimer()` waits with the dispose token, so a component being torn down stops waiting immediately. `StopRefreshTimer()` waits with `CancellationToken.None`, because it is called from `DisposeAsync()` after that token has already been cancelled, and cleanup still has to run.

## 7. Dispose the Semaphore

`SemaphoreSlim` owns a wait handle, so dispose it with the rest of the component state:

```cs
    public async ValueTask DisposeAsync()
    {
        await _disposeCancellationTokenSource.CancelAsync().ConfigureAwait(false);

        await StopRefreshTimer().ConfigureAwait(false);

        _disposeCancellationTokenSource.Dispose();
        _timerSemaphore.Dispose();

        GC.SuppressFinalize(this);
    }
```

The order matters, and it is worth being precise about what it buys you. Cancelling first is what actually stops work that is already running, because the token inside the timer callback is linked to this one. Stopping the timer next means no new refresh starts. Disposing the cancellation source and the semaphore last means nothing disappears while the callback is still using it.

One caveat, because someone will ask. `Timer.DisposeAsync()` waits for the callback delegate to return, and the callback here is an async lambda handed to `TimerCallback`, which returns `void`. An async void method returns to its caller at its first `await`, so disposing the timer does not by itself wait for an in-flight refresh to finish. Cancellation is what stops that refresh, which is the real reason cancel comes first.

## 8. Run It and Count

From the **2. Finish** folder, run the finished project and open [http://localhost:5006](http://localhost:5006):

```console
dotnet run --project StockWatch/StockWatch.csproj
```

1. Count the cards. There are 60, on the first paint and after every refresh.
2. The "quotes applied" tile reads exactly 60 when the page first paints.
3. Two seconds later it reads 120, then 180, then 240. Exactly 60 per tick, every tick.
4. Reload the page eight times. You get the same answer eight times.

That last point is the whole lesson. The starter app was not slower or louder than this one. It was just unpredictable, and unpredictable is the part you cannot debug at 3am.

## 9. Compare Against Finish

Compare your implementation with the completed file:

[2. Finish/StockWatch/Components/Pages/Dashboard.razor.cs](2.%20Finish/StockWatch/Components/Pages/Dashboard.razor.cs)

Then open the two side by side and read the diff against the starter:

[1. Start/StockWatch/Components/Pages/Dashboard.razor.cs](1.%20Start/StockWatch/Components/Pages/Dashboard.razor.cs)

Focus on the reasons behind each change, not only whether your code is textually identical. Concurrent collections are not free: they buy safety with extra allocations and extra bookkeeping, and a plain `Dictionary` or `List` is still the right choice when only one thread ever touches it.
