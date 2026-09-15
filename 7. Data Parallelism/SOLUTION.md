# Solution Walkthrough: Data Parallelism

Use this during the guided walkthrough after the challenge and group review in [README.md](README.md).

The completed project is **2. Finish/ImportPortal**, which runs at [http://localhost:5012](http://localhost:5012). Compare your decisions with the finished Blazor sample as we rebuild the import one stage at a time. Each section below names the workshop step it makes pass, so you can follow along in the workshop guide beside the Import page.

**1. Start** and **2. Finish** share the same workshop plumbing: the `Steps` and `Verification` folders, the workshop guide in `Components/Layout/WorkshopGuide.razor`, and the call counters in `CustomerApiService`. The only differences are `Services/ImportService.cs` and the port in `Properties/launchSettings.json`.

## 1. Start From the Numbers

The starter, importing 4,000 rows on a machine that reports 16 processors:

1. Validate: 4,000 of 4,000 rows in 2.12 seconds, all on one thread.
2. Enrich: 0 of 4,000 rows in 0.01 seconds.
3. Summarize: a single-threaded LINQ query over rows that are already in memory.

The finished app, same machine, same 4,000 rows:

1. Validate: 4,000 of 4,000 rows in 0.18 seconds.
2. Enrich: 4,000 of 4,000 rows in 1.44 seconds.

Read those two lists together. The finished app makes 4,000 API calls that the starter never made, and it still finishes the whole import sooner. Correctness and speed turned out to be the same fix.

## 2. Create ParallelOptions for the CPU Stage (Steps 1 and 3)

Every `Parallel` method has an overload that takes a `ParallelOptions`, so build one at the top of the method for the CPU-bound stage. The enrichment stage gets its own in section 5, because it needs a completely different ceiling:

```cs
var parallelOptions = new ParallelOptions
{
    CancellationToken = token,
    MaxDegreeOfParallelism = Environment.ProcessorCount,
};
```

`CancellationToken` is the token `RunImportAsync` was handed. With it set, the loop checks between iterations and throws `OperationCanceledException` out of the `Parallel` call when cancellation is requested, instead of running to the end of a job nobody is waiting for anymore.

This is the line Step 3 depends on. It starts an import with a token that is already cancelled and measures the CPU time the import used before it threw. With the token on these options, `Parallel.ForEach` throws before it scores a single row. Without it, the import scores every row first and only then notices the cancellation at the enrichment stage, having burned about as much CPU time as a full import.

`MaxDegreeOfParallelism` caps how many iterations run at once. For `Parallel.ForEach` it defaults to `-1`, which lets the runtime decide and leaves the thread pool as the only real limit. `Environment.ProcessorCount` is the right ceiling for CPU-bound work: more threads than cores does not create more cores, it only adds context switching to the same amount of work.

## 3. Validate Every Row On Every Core (Step 1)

The starter scores rows one at a time:

```cs
// ToDo Refactor (Step 1): every row is scored on one thread while the other
// cores sit idle. Nothing here depends on the row before it.
// ToDo Refactor (Step 3): this loop never looks at the token, so a cancelled
// import still scores every row before anything notices.
foreach (var order in orders)
{
    order.RiskScore = ScoreRisk(order);
}
```

The validation loop becomes one line:

```cs
// Step 1: validate every row. CPU-bound, so use every core.
var validateStopwatch = Stopwatch.StartNew();

Parallel.ForEach(orders, parallelOptions, static order => order.RiskScore = ScoreRisk(order));

validateStopwatch.Stop();
```

This transformation is safe for one specific reason, and it is worth saying out loud: each iteration writes to `RiskScore` on its own `OrderRow` and reads nothing another iteration wrote. There is no shared counter, no shared list, and therefore no lock. If the body had been appending to a `List<OrderRow>`, this same change would have corrupted the list.

The lambda is `static`, which means it captures nothing from the enclosing scope. That is possible because `ScoreRisk` is a static method. Making parallel lambdas `static` when you can is a cheap way to prove to yourself, and to the next reviewer, that the body has no shared state to fight over.

`Parallel.ForEach` returns a `ParallelLoopResult`. Nothing here calls `Break()` or `Stop()`, so `IsCompleted` would always be `true` and the sample ignores it. In a loop that can end early, check it.

One honest caveat about this sample. `Parallel.ForEach` blocks, and here it blocks Blazor's circuit, because `RunImportAsync` is reached from the page's `@onclick` handler. Nothing on that circuit, including the workshop guide beside the page, can process another event until the CPU stage finishes. That is acceptable for a demo whose only other UI is a spinner and a guide that can wait a fifth of a second. In an app where the circuit has real work to do, hand the blocking loop to the thread pool with `await Task.Run(() => Parallel.ForEach(orders, parallelOptions, ...), token)` so the circuit stays responsive while the cores stay busy.

On the reference machine this stage went from 2.12 seconds to 0.18 seconds.

Step 1 does not trust a stopwatch alone, because a faster laptop would make the starter look fixed. It compares the CPU time the import used with how long validation took. One thread can use at most one second of CPU time per second, however hard it works, so the starter keeps about 1.0 cores busy. The finished loop keeps close to every processor busy, and the step asks for at least 2 cores, or 1.5 on a machine with 2 or 3 processors.

## 4. Understand Why Enrichment Enriched Nothing (Step 2)

Before fixing the second stage, look at exactly what the starter did:

```cs
// ToDo Refactor (Step 2): Parallel.ForEach takes an Action, not a Func<Task>.
// This lambda is `async void`: ForEach starts each one and immediately
// considers it finished, so this returns long before any call completes
// and any exception inside it is rethrown where nothing can catch it.
Parallel.ForEach(orders, async order =>
{
    order.CustomerTier = await customerApi.GetCustomerTierAsync(order.Sku, token).ConfigureAwait(false);
});
```

`Parallel.ForEach` has no overload that takes a `Func<T, Task>`. The body it accepts here is an `Action<T>`, which returns `void`. When the compiler binds an `async` lambda to a delegate that returns `void`, it produces an `async void` method. That is legal C#, it is occasionally what you want for an event handler, and it produces no warning here.

At runtime, `Parallel.ForEach` calls the lambda, the lambda runs until its first `await`, and it returns. `ForEach` treats that as a finished item and moves on. Repeat 4,000 times and the loop is done in 0.01 seconds, which is simply the cost of starting 4,000 state machines. The count at the end of the method runs long before any 10 millisecond delay has elapsed, so it reports 0 rows enriched.

The other half of the damage does not show up in this sample, because nothing here throws. An exception inside an `async void` body never reaches the caller, and it is not swallowed either. The state machine rethrows it on the captured `SynchronizationContext`, or on the thread pool when there is none, where nothing is waiting to catch it. On the thread pool that is an unhandled exception, and an unhandled exception takes the process down. So you get neither the data nor a catchable error: the `try` block you wrote around the loop never runs, and what you are left with is a crash whose stack trace points at a lambda with no line of your calling code on it. Point a real customer API at this loop, let it return one 500, and that is your night.

The workshop steps had to design around exactly this. A cancelled `CancellationToken` makes `Task.Delay` throw inside `GetCustomerTierAsync`, so handing the starter a token that gets cancelled would end the whole app, verifier and all. That is why Steps 1 and 2 always pass `CancellationToken.None`, and why Step 3 reads `ImportService`'s compiled code to confirm no `async void` lambda is left before it cancels an import.

## 5. Enrich With Parallel.ForEachAsync (Step 2)

```cs
// Step 2: enrich every row from the customer API. I/O-bound, so use the
// asynchronous overload. It awaits every call and bounds how many run at
// once, which keeps the downstream API from being flooded.
var enrichStopwatch = Stopwatch.StartNew();

var enrichOptions = new ParallelOptions
{
    CancellationToken = token,
    MaxDegreeOfParallelism = 32,
};

await Parallel.ForEachAsync(
    orders,
    enrichOptions,
    async (order, cancellationToken) =>
        order.CustomerTier = await customerApi.GetCustomerTierAsync(order.Sku, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

enrichStopwatch.Stop();
```

Three things changed, and each one matters.

1. The body is now a `Func<OrderRow, CancellationToken, ValueTask>`. Because it returns something awaitable, `Parallel.ForEachAsync` can await it, and the `Task` it returns does not complete until every body has completed. The `await` in front of the call is what turns started work into finished work.
2. This stage gets its own `ParallelOptions` with `MaxDegreeOfParallelism = 32` instead of `Environment.ProcessorCount`. These bodies spend their time waiting on a network call, not computing, so cores are the wrong unit. The right number is whatever the downstream service will tolerate, and 32 is a number to defend, not a magic constant: unbounded parallelism against an internal API is how you take that API down with your own nightly job, and leaving the option off entirely is worse than it looks, because `Parallel.ForEachAsync` turns the `-1` default into `Environment.ProcessorCount` and quietly throttles your I/O to your core count.
3. The body uses the `cancellationToken` it is handed rather than closing over `token`. `ForEachAsync` combines the token from your options with its own internal token and passes you the result, so using the parameter is what makes cancellation actually stop the calls that are in flight.

On the reference machine this stage went from 0 rows in 0.01 seconds to 4,000 rows in 1.44 seconds. Slower on the clock, infinitely better on the only measure that counts.

Step 2 watches the calls rather than the clock. A fresh `CustomerApiService` counts how many calls are in flight, so the step can check that every row has a tier when `RunImportAsync` returns, that no call is still running at that moment, and that more than one call ran at a time without every row being sent at once. The finished app peaks at exactly 32. If you left `MaxDegreeOfParallelism` off, the step still passes, and its log points out that your peak matches the processor count, which is the default you did not choose.

One quiet side effect: the starter needed an `await` near the end of the method to justify the `async` keyword:

```cs
// ToDo Refactor (Step 2): this is the only await in the method. It yields
// the thread, but it does not wait for a single customer API call above.
await Task.Yield();
```

The finished file does not have that line, because the method now awaits something real.

## 6. Summarize With PLINQ (Step 3)

The starter groups the rows on one thread, and never looks at the token:

```cs
// ToDo Refactor (Step 3): this query runs on one thread, and it ignores the cancellation token
var regionTotals = orders
    .GroupBy(static order => order.Region)
```

```cs
// Step 3: summarize by region. PLINQ is the declarative sibling of
// Parallel.ForEach: same idea, expressed as a query.
var reportStopwatch = Stopwatch.StartNew();

var regionTotals = orders
    .AsParallel()
    .WithCancellation(token)
    .WithDegreeOfParallelism(Environment.ProcessorCount)
    .GroupBy(static order => order.Region)
    .Select(static group => new RegionTotal(
        group.Key,
        group.Count(),
        group.Sum(static order => order.Amount),
        (long)group.Average(static order => order.RiskScore)))
    .OrderBy(static total => total.Region)
    .ToList();

reportStopwatch.Stop();
```

The query body did not change at all. `AsParallel()` moves it from `Enumerable` to `ParallelEnumerable`, which partitions the source, runs the operators on each partition, and merges the results. `WithCancellation(token)` lets the query stop if the import is cancelled. PLINQ checks the token as it works through elements rather than waiting for a partition to finish, so cancellation surfaces promptly and arrives as an `OperationCanceledException`.

`WithDegreeOfParallelism` makes the ceiling explicit rather than implicit. PLINQ already defaults to `Environment.ProcessorCount`, so passing that value changes nothing at runtime, it documents the decision. This is the knob you turn *down* below the processor count inside a server that has other requests to serve, so that one report does not claim the whole machine.

Be honest about this stage. Four groups over 4,000 rows is close to the line where parallel stops paying for itself, and the merge and grouping overhead eats most of what the extra threads win. It is in the sample because the syntax is worth having in your hands, and because it is the right place to say the quiet part: PLINQ analyzes your query and may run it sequentially anyway if it decides the parallel algorithm is more expensive. `WithExecutionMode(ParallelExecutionMode.ForceParallelism)` overrides that decision, and you should only reach for it after you have measured.

The final `OrderBy` gives a deterministic order in the report even though PLINQ does not preserve source order by default. When you need the original ordering rather than a sort, that is what `AsOrdered()` is for, and it is not free.

A one millisecond query leaves nothing to time, so Step 3 checks this stage two ways. It compares the regional summary with totals worked out straight from the uploaded file, and it reads `ImportService`'s compiled code for calls to `AsParallel()` and `WithCancellation()`.

## 7. Report What Actually Happened

```cs
return new ImportReport(
    orders.Count(static order => order.RiskScore > 0),
    orders.Count(static order => order.CustomerTier is not null),
    validateStopwatch.Elapsed,
    enrichStopwatch.Elapsed,
    reportStopwatch.Elapsed,
    regionTotals);
```

Nothing about this line changed between Start and Finish, and that is the point. The counts are recomputed from the rows themselves rather than from a counter the loops maintained. The starter reported 0 enriched rows because 0 rows were enriched, not because the count was wrong. Once the enrichment stage really awaits its work, the same expression reports 4,000, and the card in the browser turns green.

## 8. What We Chose Not To Parallelize

`OrderFileService.ReadOrders(int)` still builds its `List<OrderRow>` in a plain `for` loop. Parallelizing it would mean many threads calling `List<T>.Add` at the same time, and `List<T>` is not thread safe. You would get lost rows, duplicated slots, or an exception from inside the list itself. Making that safe is the subject of the next section.

`ScoreRisk(OrderRow)` also stays exactly as it was. The lesson here is to spread expensive work across the cores you already paid for, not to make the work cheaper. Real validation passes hash, parse, and run rules, and they do not get faster because you wanted them to.

The blocking `Parallel.ForEach` in section 3 also stays on the circuit's thread, for the reason given there: this page has nothing else to do while the import runs. That is a deliberate choice for a teaching sample, not a pattern to copy into a page that has other work.

## 9. Compare Against Finish

Compare your implementation with the completed file:

[2. Finish/ImportPortal/Services/ImportService.cs](2.%20Finish/ImportPortal/Services/ImportService.cs)

And with the code you started from:

[1. Start/ImportPortal/Services/ImportService.cs](1.%20Start/ImportPortal/Services/ImportService.cs)

Run both apps side by side, [http://localhost:5011](http://localhost:5011) and [http://localhost:5012](http://localhost:5012). The finished app's workshop guide shows 3 of 3 steps pass once the checks that run after the app starts have finished, while the starter's stops at Step 1. Then click **Run import** on the Import page in each. Focus on the reasons behind each change, not only on whether your code is textually identical to mine.
