# Solution Walkthrough: Channels

Use this during the guided walkthrough after the challenge and group review in [README.md](README.md).

The completed project is **2. Finish/TelemetryPipeline**. Every real change happens in `Services/TelemetryIngestService.cs`. `EventStore`, `Program.cs`, `Models/TelemetryEvent.cs`, and the Blazor page do not change at all, `TelemetryProcessor` picks up a comment, and the only other difference is the port in `Properties/launchSettings.json`, so the finished copy can run beside your own. That is the point of this pattern: the producer and the consumer never had to learn anything about each other.

## 1. Reference the Channels Namespace

`Channel`, `Channel<T>`, `ChannelReader<T>`, `ChannelWriter<T>`, and `BoundedChannelOptions` all live in one namespace:

```cs
using System.Diagnostics;
using System.Threading.Channels;
```

`System.Threading.Channels` ships in the shared framework, so there is no package to install.

## 2. Create the Bounded Channel

The `List<TelemetryEvent>` goes away and a bounded channel takes its place:

```cs
    const int _queueCapacity = 500;
    const int _consumerCount = 8;

    // A bounded channel is the whole pattern: a thread-safe queue with a
    // capacity. The capacity is the backpressure knob. It absorbs a burst,
    // and once it is full WriteAsync slows the producer instead of losing work.
    readonly Channel<TelemetryEvent> _events = Channel.CreateBounded<TelemetryEvent>(
        new BoundedChannelOptions(_queueCapacity)
        {
            // Wait applies backpressure. DropOldest or DropWrite would instead
            // shed load, which is the right call for metrics you can afford to lose.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
```

Three decisions are packed into those options.

`Capacity` is passed to the `BoundedChannelOptions` constructor. It is the backpressure knob, and it is what makes a bounded channel a design tool instead of only a container. 500 is larger than the 400 event burst on purpose, so the burst is absorbed whole and no device ever waits. Pick that number from something real in your own systems: how much work you can afford to lose in a crash, or how much memory a queued item costs.

`FullMode` decides what happens on the write that does not fit. `Wait` makes the producer wait for room. The alternatives, `DropNewest`, `DropOldest`, and `DropWrite`, all make the write succeed immediately by throwing an item away instead, with no exception and no log line. If you need to know what was dropped, `Channel.CreateBounded` has an overload that takes an `itemDropped` callback and invokes it with every item the channel discards.

`SingleReader` and `SingleWriter` are promises you make to the runtime so it can pick a cheaper implementation. They are not enforced. Both are `false` here because every browser session can post events and eight consumers read the same channel. A broken promise here does not throw, it corrupts, so leave these `false` unless the code structurally guarantees otherwise.

## 3. Make the Counters Thread Safe

The counters are now touched by the producer and by eight consumers at the same time:

```cs
    int _accepted;
    int _processed;

    public int Accepted => Volatile.Read(ref _accepted);

    public int Processed => Volatile.Read(ref _processed);

    public int QueueDepth => _events.Reader.Count;
```

`_accepted++` is a read, an add, and a write, which is why the increments in the next two steps use `Interlocked`. `Volatile.Read` on the way out is the matching half: it stops the compiler and the CPU from handing the page a value cached in a register, and it guarantees the page never reads a value older than the last increment it can observe. It does not freeze the value. The consumers are still running, so what the page renders is a snapshot that is already slightly stale, and for a counter that is fine.

`QueueDepth` no longer counts a list. `Reader.Count` is the channel's current buffered depth. It is a snapshot too, and it is the single most useful number to put on a real dashboard, because a queue depth that keeps climbing tells you which half of your pipeline is losing.

## 4. Accept Without Touching the Database

This is the fix the whole section is about:

```cs
    // Returns as soon as the event is queued. The device is not waiting on the database.
    public async ValueTask AcceptAsync(TelemetryEvent telemetryEvent, CancellationToken token)
    {
        await _events.Writer.WriteAsync(telemetryEvent, token).ConfigureAwait(false);

        Interlocked.Increment(ref _accepted);
    }
```

The call to `eventStore.SaveAsync(...)` is gone from the accept path. Accepting an event now means putting it in the queue, and that is all it means. The 40 millisecond write still happens, just not while a device is holding the connection open.

`ValueTask` is the right return type here because `WriteAsync` almost always completes synchronously. There is room in the buffer, the item goes in, and nothing suspends. On that path a `ValueTask` allocates nothing, which matters on a call that a fleet of devices makes constantly.

That `await` is also the only place backpressure can appear. With a capacity of 500 and a burst of 400 it never suspends, which is exactly why the measured average accept time is 0.0ms. Shrink the capacity below the burst size and this same line starts waiting for room, with no other change to the code.

`Interlocked.Increment` replaces `_accepted++` because more than one caller can be in this method at once.

## 5. Write the Consumer

The polling loop is replaced by an `await foreach`:

```cs
    async Task ConsumeAsync(CancellationToken token)
    {
        await foreach (var telemetryEvent in _events.Reader.ReadAllAsync(token).ConfigureAwait(false))
        {
            await eventStore.SaveAsync(telemetryEvent, token).ConfigureAwait(false);

            Interlocked.Increment(ref _processed);
        }
    }
```

`ReadAllAsync(token)` returns an `IAsyncEnumerable<TelemetryEvent>`. The `await foreach` yields every event that was written, in order, and suspends without holding a thread when the channel is empty. That is why the busy-wait is gone: waiting for the next event is now an `await` instead of a 25 millisecond sleep in a loop.

`_processed` is now honest. It counts events that actually reached the store, which is why the page can show the queue draining after the burst has already returned.

Notice what this method does not contain. There is no lock, no `Interlocked` around the queue itself, no check to see whether the producer is still running, and no `List<T>` that two threads mutate at once. The channel is thread safe by construction.

## 6. Run Several Consumers Over One Reader

One consumer at 40 milliseconds per write can only clear 25 events per second. Start eight of them:

```cs
    // Several consumers share one reader. ReadAllAsync yields every event that
    // was ever written, then completes once the writer is done and the channel
    // is empty. No polling, no busy-waiting.
    public async Task DrainAsync(CancellationToken token)
    {
        var consumers = Enumerable
            .Range(0, _consumerCount)
            .Select(_ => ConsumeAsync(token));

        await Task.WhenAll(consumers).ConfigureAwait(false);
    }
```

Every consumer reads the same `ChannelReader<TelemetryEvent>`, and each event is handed to exactly one of them. There is no partitioning code, no coordination, and no duplicate work.

`_consumerCount` is your degree of parallelism, and it is the honest place to set that limit, because it is also how much concurrent load you are putting on the database. Eight consumers doing 50 writes each at 40 milliseconds is 2 seconds of work, and the measured drain is 2.11 seconds.

Say one thing out loud about `Task.WhenAll` here, because it is the trap in this shape. It does not fail fast. `EventStore.SaveAsync` in this sample cannot throw, but in your own pipeline it can. If one consumer throws, the other seven keep reading, `DrainAsync` does not complete, and nobody observes that fault until every consumer has finished, which for a pipeline that runs for the life of the process means shutdown. You would silently be down one eighth of your throughput. That is why a production consumer puts a `try` and `catch` inside the `await foreach`, around the work for a single event, so one poison event costs you one event instead of one consumer.

Be clear about ordering too. Events leave the channel in the order they were written, but once eight consumers are running, completion order is whatever the store decides. If per device ordering matters in your own systems, give each device its own channel or key the work so one device always lands on the same consumer.

## 7. Complete the Writer

```cs
    // Marks the channel complete so DrainAsync finishes after the backlog is written
    public void CompleteWriting() => _events.Writer.TryComplete();
```

`ReadAllAsync` has no way to know that no more events are coming. Without a completion signal the `await foreach` waits forever, `Task.WhenAll` never returns, and a pipeline like this hangs on shutdown. That is the most common channel bug there is.

Completing does two things. It rejects every later write, with `WriteAsync` throwing `ChannelClosedException` while `TryWrite` and `WaitToWriteAsync` return `false`, and it lets `ReadAllAsync` finish once the buffer has drained, so the backlog is still written before the loop ends.

`TryComplete()` is used instead of `Complete()` because more than one code path could reasonably close this channel. It returns `false` when the channel is already complete rather than throwing.

Nothing calls this during a burst, and that is deliberate. Call `CompleteWriting` from `RunBurstAsync` and the first burst still passes every acceptance check, but the second click of **Receive 400 events** throws, because the channel is closed for good. This is the method you call exactly once, when the app is going away, and that is the next step.

## 8. Report From the Channel

`RunBurstAsync` keeps its shape, and the first thing to point at is what is missing:

```cs
    public async Task<PipelineStats> RunBurstAsync(int eventCount, CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        var totalAcceptMilliseconds = 0d;
        var slowestAccept = 0d;
```

The call to `Reset()` is gone, and so is the method. Clearing the counters mid flight would now be wrong, because the consumers are still draining the previous burst in the background. The pipeline outlives any single button click, so the counters are cumulative. Restart the app when you want a clean measurement.

The stats now come from the channel and the atomic counters instead of a list:

```cs
        return new PipelineStats(
            Accepted,
            Processed,
            0,
            QueueDepth,
            totalAcceptMilliseconds / eventCount,
            slowestAccept,
            stopwatch.Elapsed.TotalSeconds);
```

`Processed` and `QueueDepth` go into the record at the moment the burst returns, which is almost immediately. The **Written to store** card does not read that snapshot though. `Ingest.razor` binds it to `LiveProcessed` and `LiveQueueDepth`, and `Ingest.razor.cs` defines those as `Ingest.Processed` and `Ingest.QueueDepth`, so the card reads the singleton every time the page renders. That is why the card shows a full queue and an empty store the instant the burst returns, and why **Refresh counters** is what lets you watch it drain.

## 9. Drain the Backlog on Shutdown

`TelemetryProcessor` is where completion finally gets used:

```cs
namespace TelemetryPipeline;

// The consumer half of the pipeline. A BackgroundService is the standard place
// to run a channel reader for the lifetime of the application.
public sealed class TelemetryProcessor(TelemetryIngestService ingest) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Deliberately not stoppingToken. Cancelling the read loop would abandon
        // whatever is still queued, and every reading is supposed to reach the store.
        // StopAsync closes the channel instead, which ends ReadAllAsync once it drains.
        return ingest.DrainAsync(CancellationToken.None);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop accepting new readings first, so the consumers can finish the backlog
        ingest.CompleteWriting();

        // Cancels stoppingToken, then waits for ExecuteAsync to return.
        // The wait is bounded by the host's shutdown timeout, so a pipeline that
        // cannot drain in time still lets the process exit.
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

A `BackgroundService` is the standard place to run a channel reader in ASP.NET Core. The host starts `ExecuteAsync` when the app starts and calls `StopAsync` when it stops.

The obvious version of this method takes the `stoppingToken` and passes it into `DrainAsync`, and it is wrong in a way that is easy to miss. `BackgroundService.StopAsync` cancels that token and then waits for `ExecuteAsync` to return. Cancelling is what tears down the `await foreach`, so every reading still sitting in the channel is thrown away. Measure it and the pipeline writes 0 of 400 on shutdown: the burst is accepted, the device is told everything is fine, and the readings never reach the store.

So the read loop does not take the token at all. Shutdown is signalled by completing the channel instead. `ReadAllAsync` drains what is buffered and then ends on its own, `Task.WhenAll` returns, `ExecuteAsync` returns, and `base.StopAsync` sees the task finish. With completion wired in, the same run writes 400 of 400 and shutdown takes about two seconds instead of being instant.

Two things keep this honest. `base.StopAsync` bounds its wait with the host's shutdown timeout, so a backlog that cannot drain in time does not hang the process forever. And completing the channel means a request still trying to accept a reading during shutdown gets a `ChannelClosedException`, which is the correct answer: the app is going away and cannot promise to store it.

The registration in `Program.cs` did not change either:

```cs
        builder.Services.AddSingleton<EventStore>();
        builder.Services.AddSingleton<TelemetryIngestService>();
        builder.Services.AddHostedService<TelemetryProcessor>();
```

`TelemetryIngestService` is a singleton, so the page's producer and the hosted service's consumers share one channel. If your consumer needs a scoped service such as a database context, create the scope inside the loop, per item, because the hosted service itself is a singleton.

## 10. Run It and Read the Numbers

From the **9. Channels** folder, run the finished project:

```console
dotnet run --project "2. Finish/TelemetryPipeline/TelemetryPipeline.csproj"
```

Open [http://localhost:5014](http://localhost:5014) and click **Receive 400 events**. To watch your own refactor instead, run the project in **1. Start** and open [http://localhost:5013](http://localhost:5013). The two ports differ so both copies can run side by side.

Three numbers are worth pointing at on screen.

First, **Average accept** reads 0.0ms, down from 41.4ms. The card is green because it is under the 5ms threshold, and it is well under the 20ms a device will wait. Nothing about the database got faster. The write simply stopped happening while the device was watching.

Second, **Burst duration** reads 0.00s, down from 16.56s. The endpoint took 400 readings and was free again immediately.

Third, **Written to store** starts near zero with the rest of the burst waiting in the queue. Click **Refresh counters** once a second and watch the written count climb while the waiting count falls. About 2.11 seconds after the burst, all 400 events have reached the store. The work did not get skipped, it got moved.

## 11. Feel the Backpressure

Change `_queueCapacity` from `500` to `50` and run the burst again. The burst no longer fits in the buffer, so `WriteAsync` starts waiting for room, and the average accept time climbs off zero. The producer has been throttled to the speed of the consumers, and no code in the app measured anything or decided to slow down. That single `await` did it.

Now change `FullMode` to `BoundedChannelFullMode.DropOldest` and run it once more. Every write completes immediately again, the accept time drops back toward zero, and the written count settles below 400. Those events are gone, with no exception and no log line to tell you.

That is the right behavior for a live gauge where only the latest reading matters, and the wrong behavior for readings a customer is billed for. Choosing between `Wait`, `DropNewest`, `DropOldest`, and `DropWrite` is a business decision, not a performance decision, and it is worth a comment next to the option so the person who edits this file in six months knows why.

## 12. Compare Against Finish

Compare your implementation with the completed files:

1. [2. Finish/TelemetryPipeline/Services/TelemetryIngestService.cs](2.%20Finish/TelemetryPipeline/Services/TelemetryIngestService.cs)
2. [2. Finish/TelemetryPipeline/Services/TelemetryProcessor.cs](2.%20Finish/TelemetryPipeline/Services/TelemetryProcessor.cs)
3. [2. Finish/TelemetryPipeline/Services/EventStore.cs](2.%20Finish/TelemetryPipeline/Services/EventStore.cs)

`EventStore.cs`, `Program.cs`, `Models/TelemetryEvent.cs`, and the Blazor page are identical between **1. Start** and **2. Finish**. The slow database write is still slow, the page still asks for 400 events, and the hosted service still calls `DrainAsync`. All that changed is where the work happens, and a channel is what made that change one file instead of a rewrite.
