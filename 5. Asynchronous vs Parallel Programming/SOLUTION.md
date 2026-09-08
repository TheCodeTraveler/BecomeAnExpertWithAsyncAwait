# Solution Walkthrough: Asynchronous vs Parallel Programming

Use this during the guided walkthrough after the challenge and group review in [README.md](README.md).

The completed project is **2. Finish/OrderPortal**. Compare your implementation with the finished sample as we rebuild the solution step by step.

## 1. Find the Shared State

Before changing a line, notice why these bugs exist at all. Four services are registered as singletons in **Program.cs**:

```cs
// Registered as singletons, so one instance is shared by every
// concurrent request. That ensures their state is a shared resource.
builder.Services.AddSingleton<OrderMetrics>();
builder.Services.AddSingleton<TaxRateProvider>();
builder.Services.AddSingleton<InventoryLedger>();
builder.Services.AddSingleton<CheckoutService>();
```

One `OrderMetrics`, one `TaxRateProvider` and one `InventoryLedger` for the whole application. That is the normal, correct choice for services that hold a cache or a counter. It also means that when `CheckoutService.RunCheckoutBurstAsync(int, CancellationToken)` pushes 2,000 checkouts through `Parallel.ForEachAsync`, one worker per core is inside the same three objects at every instant, and all 2,000 orders pass through them over the life of the burst. `Parallel.ForEachAsync` defaults `MaxDegreeOfParallelism` to `Environment.ProcessorCount`, and that number is exactly why you will see the tax table built once per core in Step 4.

Nothing about that is exotic. Any service you register with `AddSingleton` is reachable from every concurrent request your server is handling, and every mutable field on it is shared state.

## 2. Make the Order Count Atomic

The starter records an order like this:

```cs
// ToDo Refactor: `++` is a read, an add, and a write. Two threads can read the
// same value before either writes, and one order silently disappears.
public void RecordOrder(decimal orderTotal)
{
    _ordersPlaced++;
    _revenue += orderTotal;
}
```

`_ordersPlaced++` looks like one step. It is three: read the current value, add one, write the result back. Two threads can read `1`, both add one, and both write `2`. Two orders happened and the counter moved by one. That is the missing 4 orders out of 2,000.

`Interlocked` performs the read, the add and the write as a single atomic operation that no other thread can interleave with:

```cs
// One atomic instruction. Nothing can slip between the read and the write.
Interlocked.Increment(ref _ordersPlaced);
```

`Interlocked` is the cheapest tool in the box. It is lock free, so no thread ever waits. `Increment(ref int)`, `Decrement(ref int)` and `Add(ref int, int)` work on `int` and `long`, and `Exchange` and `CompareExchange` also handle `float`, `double`, `nint` and object references.

## 3. Guard the Revenue With a Lock

`Interlocked` has no `decimal` overload, because `decimal` is 128 bits and cannot be updated as a single atomic instruction. That is also why the Revenue card is short by much more than the missing orders are worth: `decimal +=` is a far wider read, modify, write than `int++`, so it loses many more updates. Revenue needs a real lock:

```cs
// Interlocked has no decimal overload, so revenue is guarded by a lock.
// The .NET 9 Lock type is a dedicated lock object rather than locking on `object`.
readonly Lock _revenueLock = new();
```

`Lock` is the .NET 9 type in `System.Threading`. It is a dedicated lock object instead of locking on an arbitrary `object`, and the familiar `lock` statement understands it. Before .NET 9 a `readonly object` field does the same job.

Here is the finished `OrderMetrics` in full. Note that the reads are guarded too. `OrdersPlaced` uses `Volatile.Read` so the getter cannot hand back a stale cached value, and `Revenue` reads inside the same lock that writes it, so it can never observe a half written `decimal`. Reads matter as much as writes:

```cs
    public int OrdersPlaced => Volatile.Read(ref _ordersPlaced);

    public decimal Revenue
    {
        get
        {
            lock (_revenueLock)
            {
                return _revenue;
            }
        }
    }

    public void RecordOrder(decimal orderTotal)
    {
        // One atomic instruction. Nothing can slip between the read and the write.
        Interlocked.Increment(ref _ordersPlaced);

        lock (_revenueLock)
        {
            _revenue += orderTotal;
        }
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _ordersPlaced, 0);

        lock (_revenueLock)
        {
            _revenue = 0;
        }
    }
```

`Reset()` gets the same treatment. It runs at the start of every burst, so it is shared state too.

## 4. Build the Tax Table Exactly Once

The starter caches the rate table in a nullable field:

```cs
    // ToDo Refactor: two threads can both find this null and both build the table
    IReadOnlyDictionary<string, decimal>? _rates;
```

```cs
        // ToDo Refactor: `??=` is not atomic. Under load this runs BuildRates()
        // many times, and every caller pays the full build cost.
        _rates ??= BuildRates();
```

`??=` reads as one operation and compiles into two: check whether `_rates` is null, and if it is, assign it. `Parallel.ForEachAsync` runs one checkout per core by default, so on a 16 core machine 16 threads got between the check and the assignment, `BuildRates()` ran 16 times, and each of them paid the full 120 millisecond cost. Your own run shows one build per core, so the number on your screen is your core count.

`Lazy<T>` exists for exactly this. Its default thread safety mode, `LazyThreadSafetyMode.ExecutionAndPublication`, guarantees the factory runs exactly once and that every caller gets the same instance:

```cs
    // Lazy<T> guarantees the factory runs exactly once no matter how many threads
    // hit Value at the same time. That is its default thread safety mode,
    // LazyThreadSafetyMode.ExecutionAndPublication.
    Lazy<IReadOnlyDictionary<string, decimal>> _rates;

    public TaxRateProvider()
    {
        _rates = CreateRatesLazy();
    }
```

`GetRate(string)` then just asks for `Value`. The first caller to reach it runs the factory while the rest wait, and every later call is a plain field read:

```cs
    public decimal GetRate(string region)
    {
        Interlocked.Increment(ref _lookups);

        return _rates.Value.TryGetValue(region, out var rate) ? rate : 0m;
    }
```

The lookup counter gets `Interlocked.Increment` for the same reason the order count did.

## 5. Keep Reset Honest

`Reset()` cannot set a `Lazy<T>` back to "not created yet", so it hands out a fresh one:

```cs
    public void Reset()
    {
        Interlocked.Exchange(ref _lookups, 0);
        Interlocked.Exchange(ref _builds, 0);

        _rates = CreateRatesLazy();
    }

    Lazy<IReadOnlyDictionary<string, decimal>> CreateRatesLazy() => new(BuildRates);
```

That keeps the demo repeatable. Press the button a second time and the table is built exactly once again, not zero times and not once per core.

`_rates` itself is swapped with a plain write, which is safe because `Reset()` can only run before a burst, never during one. Reference assignment is atomic, so you can never read a torn `Lazy<T>`, but agreeing on which `Lazy<T>` is a separate problem. If a reset could overlap a burst, two callers could each hold a different instance and the table would build twice. `Lazy<T>` guarantees one execution per instance, not one per field.

That "can only" is a guarantee rather than a hope, and it is worth seeing where it comes from. The Run button is disabled while a burst is running, but a disabled button only covers one browser tab, and `CheckoutService` is a singleton that every tab shares. So the burst takes a lock of its own:

```cs
await _burstSemaphore.WaitAsync(token).ConfigureAwait(false);
```

`SemaphoreSlim` for the same reason `InventoryLedger` uses one: the burst awaits, and `lock` cannot be held across an `await`. Notice what is being guarded, though. `Interlocked` and `Lock` each protect a single update, but the invariant here spans reset, then measure, then read, so the exclusion has to span all three. Sometimes the right unit is not the field. It is the whole operation.

This one is not part of your challenge. It is in **1. Start** and **2. Finish** alike, already correct, and it is why pressing Run in two tabs at once gives you two honest answers instead of two scrambled ones.

The counters are read through `Volatile.Read`. `RunCheckoutBurstAsync(int, CancellationToken)` awaits the burst to completion before it reads them, and that `await` is already a memory barrier, but these are public getters on a singleton and any thread can call them at any time. Nothing forces the compiler or the CPU to hand a reader the freshest value of a field another thread is writing, and `Volatile.Read` is how you say you want it:

```cs
    public int Lookups => Volatile.Read(ref _lookups);

    // How many times the expensive table was actually built. Always 1.
    public int Builds => Volatile.Read(ref _builds);
```

## 6. Read the Deadlock Before Fixing It

Nothing about the ledger is careless. It uses `SemaphoreSlim` rather than `lock`, which is right: `lock` cannot be held across an `await`, and this code awaits. The bug is one line:

```cs
            // ToDo Refactor: this call also waits on _ledgerSemaphore, which this
            // method is already holding. SemaphoreSlim is not reentrant, so the
            // thread waits for a permit it will never release. That is a deadlock.
            await WriteAuditEntryAsync($"Reserved {quantity} of {sku}", token).ConfigureAwait(false);
```

`ReserveStockAsync(...)` is holding `_ledgerSemaphore`. `WriteAuditEntryAsync(...)` begins by waiting on `_ledgerSemaphore`. The semaphore has one permit, and the caller that wants it is the caller that already holds it. `SemaphoreSlim` is not reentrant, so it will not notice that this is the same caller. It waits forever.

In the browser that shows up as the request sitting there until the 5 second timeout cancels it. In production there is no timeout, so the request never completes. Notice what does not happen: no thread is blocked. `WaitAsync` is awaited, so the thread went straight back to the pool and the operation is simply parked forever. Nothing is pegged, nothing is starved, and a thread dump shows nothing waiting, which is exactly why this is so easy to miss. What is stuck is the permit. The ledger holds its only permit forever, so every later caller of `ReserveStockAsync(...)` and `WriteAuditEntryAsync(...)` queues up behind a lock that will never be released.

## 7. Split the Ledger in Two

The fix is a pattern worth memorizing: a public method that takes the lock, and a private `...CoreAsync` method that assumes the lock is already held. Every caller that needs the lock takes it exactly once.

```cs
    public async Task<bool> ReserveStockAsync(string sku, int quantity, CancellationToken token)
    {
        await _ledgerSemaphore.WaitAsync(token).ConfigureAwait(false);

        try
        {
            if (!_stockOnHand.TryGetValue(sku, out var onHand) || onHand < quantity)
            {
                return false;
            }

            // Calls the version that does NOT take the semaphore, because this
            // method is already holding it.
            await WriteAuditEntryCoreAsync($"Reserved {quantity} of {sku}", token).ConfigureAwait(false);

            // The audit write above can be cancelled, so the decrement happens
            // after it. A cancelled call must not reduce stock with no audit entry.
            _stockOnHand[sku] = onHand - quantity;

            return true;
        }
        finally
        {
            _ledgerSemaphore.Release();
        }
    }

    public async Task WriteAuditEntryAsync(string entry, CancellationToken token)
    {
        await _ledgerSemaphore.WaitAsync(token).ConfigureAwait(false);

        try
        {
            await WriteAuditEntryCoreAsync(entry, token).ConfigureAwait(false);
        }
        finally
        {
            _ledgerSemaphore.Release();
        }
    }

    // Must only be called while _ledgerSemaphore is already held
    async Task WriteAuditEntryCoreAsync(string entry, CancellationToken token)
    {
        // Pretend this writes to an audit table
        await Task.Delay(TimeSpan.FromMilliseconds(5), token).ConfigureAwait(false);

        lock (_auditTrail)
        {
            _auditTrail.Add(entry);
        }
    }
```

`ReserveStockAsync(...)` takes the permit and calls `WriteAuditEntryCoreAsync(...)`, which never touches the semaphore. `WriteAuditEntryAsync(...)` is still there for callers that do not hold the lock, and it takes the permit itself. The stock update and its audit entry still happen under one lock, so the ledger is still atomic. Nothing waits for itself.

Order matters inside that lock, and it is worth saying why. The audit write is the only step that can be cancelled, because it is the only one that awaits, so it runs first and the decrement follows it. Put the decrement first and a token that trips during that 5 millisecond write leaves stock reduced with no audit entry, which is exactly what the comment at the top of the class promises never happens. Measured over 200 reservations cancelled mid write, the decrement-first ordering left the ledger inconsistent every single time and the audit-first ordering never did. The rule generalises: inside a lock, do the work that can fail before the work you cannot undo.

Notice the naming convention. The `Core` suffix is the signal that says "this method assumes the lock is held". Add the comment above it too. Six months from now, that comment is the only thing standing between someone and a reintroduced deadlock.

## 8. Guard the Audit Trail

`List<T>` is not thread safe. In the starter, `AuditEntries` returns `Count` with no lock at all, while the audit writer is appending to the same list:

```cs
    public int AuditEntries => _auditTrail.Count;
```

So take a lock in the getter, and take the same lock in the writer:

```cs
    public int AuditEntries
    {
        get
        {
            lock (_auditTrail)
            {
                return _auditTrail.Count;
            }
        }
    }
```

`WriteAuditEntryCoreAsync(...)` adds under that same lock, so a read can never land in the middle of an append. It is the same class of bug as the counter: a collection that only ever had one writer now has several.

While you are in the file, update the comment on the semaphore so the next reader knows the rule:

```cs
    // SemaphoreSlim is the right primitive here: `lock` cannot be held across an await.
    // It is also not reentrant, so no method that holds it may call another method
    // that takes it. The fix is to split each operation into a public method that
    // takes the semaphore and a private method that assumes it is already held.
    readonly SemaphoreSlim _ledgerSemaphore = new(1, 1);
```

## 9. Run It Five Times

Run the finished app on [http://localhost:5008](http://localhost:5008) and press **Run 2000 checkouts**. All three graded cards turn green: 2000 orders recorded, revenue matching the expected total, and the tax table built exactly 1 time. Press **Reserve stock** and it answers immediately instead of timing out.

Then run the burst four more times. That repeatability is the point. Race conditions are timing dependent, so a single passing run proves nothing; a fixed one is correct every time, on every machine in the room.

## 10. Compare Against Finish

Compare your implementation with the completed files:

[2. Finish/OrderPortal/Services/OrderMetrics.cs](2.%20Finish/OrderPortal/Services/OrderMetrics.cs)

[2. Finish/OrderPortal/Services/TaxRateProvider.cs](2.%20Finish/OrderPortal/Services/TaxRateProvider.cs)

[2. Finish/OrderPortal/Services/InventoryLedger.cs](2.%20Finish/OrderPortal/Services/InventoryLedger.cs)

Focus on why each primitive was chosen, not on whether your code is textually identical. `Interlocked` for a counter, a `Lock` for the value `Interlocked` cannot touch, `Lazy<T>` for the expensive thing that must happen once, and `SemaphoreSlim` for the lock that has to survive an `await`.
