# Asynchronous vs Parallel Programming

**OrderPortal** is a checkout system that has never failed. Every service in it was written carefully, reviewed, and tested, and for a year it has handled one customer at a time without a single incident. Then a sale goes live, two thousand people press Buy in the same second, and the revenue report stops matching the orders table. Nothing throws. Nothing logs a warning. The numbers are just quietly wrong. That is the difference between asynchronous and parallel: the instant your code runs in parallel, every field on every singleton has more than one writer, and code you have read a hundred times becomes a bug you can only reproduce under load.

The **1. Start** folder contains the intentionally imperfect code you will edit. The **2. Finish** folder contains the completed app.

## 1. Open the Starter Project

1. Using File Explorer on Windows or Finder on macOS, navigate to **BecomeAnExpertWithAsyncAwait/5. Asynchronous vs Parallel Programming/1. Start**.
2. Open **OrderPortal.slnx** in your IDE.
3. Build the project once so you can confirm the starter compiles. It builds clean, with no warnings. Every bug in this section is a runtime bug.
4. Run the app and open it in your browser.

```console
dotnet build OrderPortal.slnx
```

```console
dotnet run --project "OrderPortal/OrderPortal.csproj"
```

The app runs at [http://localhost:5007](http://localhost:5007).

## 2. Inspect the Starting Code

1. Open **OrderPortal/Program.cs** and notice that `OrderMetrics`, `TaxRateProvider`, `InventoryLedger` and `CheckoutService` are registered with `AddSingleton`. One instance of each is shared by every request.
2. Open **OrderPortal/Services/CheckoutService.cs** and read `RunCheckoutBurstAsync(int, CancellationToken)`. It queues 2,000 checkouts through `Parallel.ForEachAsync`, which runs `Environment.ProcessorCount` of them at a time by default. So roughly one checkout per core is inside those singletons at any instant, and all 2,000 pass through the same three objects over the life of the burst. One writer per core is all it takes to lose orders.
3. Open **OrderPortal/Services/OrderMetrics.cs**, **OrderPortal/Services/TaxRateProvider.cs**, and **OrderPortal/Services/InventoryLedger.cs**, and find each `// ToDo Refactor` comment.
4. Open **OrderPortal/Components/Pages/Checkout.razor.cs** and read `ExpectedRevenue`. Nothing in this app is random. Every graded result card has exactly one correct value.

Now run the app and use it:

1. Press **Run 2000 checkouts**. Four cards appear: Orders recorded, Revenue, Tax table builds, and Elapsed. The first three are graded against a known correct value: a green left edge means the number matched, and a red left edge means it did not. Elapsed stays grey, because it is a timing, not a correctness check.
2. Read the Orders recorded card. It expects 2000, and you will see something slightly lower, such as 1996. Those orders were placed. The counter lost them.
3. Read the Revenue card. It is short too, and by considerably more than the missing orders are worth, often several times more. `decimal +=` is a much wider read, modify, write than `int++`, so it loses far more updates, and unlike an `int`, a 16 byte `decimal` can also be read or written half updated. The two shortfalls will not line up no matter how carefully you divide.
4. Read the Tax table builds card. It expects 1 and will show one build per CPU core, so 16 on a 16 core machine and 8 on an 8 core laptop. Building that table takes 120 milliseconds, and it was supposed to happen once for the life of the process.
5. Read the small note under Elapsed. It reports how many tax rate lookups were counted, and that counter is a plain `++` as well.
6. Press **Run 2000 checkouts** again. Orders recorded and Revenue change every run. That is what makes these bugs expensive: they are timing dependent, they never reproduce the same way twice, and a single passing run proves nothing.
7. Press **Reserve stock**. The panel says "Reserving stock..." and then sits there. Five seconds later it gives up with a timeout message. Nothing crashed. The request is simply stuck.

Pay attention to these clues:

1. `_ordersPlaced++` is not one operation. It is a read, an add, and a write.
2. `Interlocked` has overloads for `int` and `long`. It has none for `decimal`.
3. `_rates ??= BuildRates()` is a null check followed by an assignment, with a gap in between.
4. `Reset()` runs at the start of every burst, so it touches shared state too.
5. Reads can be wrong as well as writes. An `int` read can be stale, and a `decimal` read can catch a value halfway through an update.
6. `InventoryLedger` uses `SemaphoreSlim` rather than `lock`, and that part is correct. `lock` cannot be held across an `await`.
7. `SemaphoreSlim` is not reentrant. Ask which method takes the permit, and which method it calls next.
8. `List<T>` and `Dictionary<TKey, TValue>` are not thread safe. A lock is all you need to make the one in this app safe. There are purpose built collections for this too, and we cover them this afternoon.

## 3. Challenge: Make the Shared Services Safe Under Load

Recommended time: 25 minutes.

> **Note:** Please avoid letting AI Agents solve the challenges for you. You're smart. You got this. Use them to understand the existing code, clarify parallel programming concepts, interpret errors, and ask questions that help you decide what to change. The goal is to practice the reasoning yourself.

Fix **OrderMetrics.cs**, **TaxRateProvider.cs**, and **InventoryLedger.cs** so the app is correct when 2,000 checkouts run through `Parallel.ForEachAsync`. Leave `CheckoutService`, the page, and the singleton registrations alone. The load is not the bug.

Everything you need is in this section's toolbox: `Interlocked`, `lock` with the .NET 9 `Lock` type, `Lazy<T>`, and the `SemaphoreSlim` that is already in `InventoryLedger`. You do not need anything from `System.Collections.Concurrent`. Those types are the subject of Concurrent Collections this afternoon, and reaching for one here would hide the lesson rather than teach it.

Requirements:

1. Update the order count atomically so no increment is ever lost.
2. Guard the revenue total. `Interlocked` cannot help you here, so pick the right lock for a synchronous update.
3. Make the property getters safe too, so the page cannot read a stale count or a half written `decimal`.
4. Keep `OrderMetrics.Reset()` correct when it runs against the same shared state.
5. Build the expensive tax table exactly once, no matter how many threads call `GetRate(string)` at the same moment.
6. Count tax rate lookups atomically.
7. Keep `TaxRateProvider.Reset()` working: after a reset, the next burst must build the table again, exactly once.
8. Remove the deadlock in `InventoryLedger.ReserveStockAsync(...)` without losing the guarantee that the stock update and its audit entry happen under the same lock.
9. Keep `WriteAuditEntryAsync(...)` callable on its own, by a caller that does not already hold the ledger lock.
10. Guard `_auditTrail` with a lock so it is not read while it is being written.
11. Keep `SemaphoreSlim` in `InventoryLedger`. `lock` cannot be held across an `await`, and the ledger awaits.

Acceptance checks:

1. **OrderPortal.slnx** builds.
2. Orders recorded shows 2000 with a green edge.
3. Revenue matches the expected total shown on the card, with a green edge.
4. Tax table builds shows 1 with a green edge, on your machine and on every other machine in the room.
5. **Reserve stock** answers right away with "Reserved 1 of SKU-1000 and wrote the audit entry." instead of timing out after 5 seconds.
6. Pressing **Run 2000 checkouts** five times in a row gives the same three correct numbers every time.
7. Your code is ready to compare with **2. Finish/OrderPortal**.

## 4. Review the Solution

After you have attempted the challenge, pause here for group review.

We will compare approaches, talk through why each primitive fits the state it is guarding, and answer questions before opening [SOLUTION.md](SOLUTION.md) together.

The solution walkthrough shows the order to make each fix and points you to the completed code in **2. Finish/OrderPortal**.