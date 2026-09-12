# Channels

In this section, you will fix a device telemetry webhook that makes every device wait for a database write. `TelemetryPipeline` is the server side of a device fleet. Each device posts a reading, every reading has to reach the database, and the database takes 40 milliseconds per write. The devices do not care how long that write takes. They care how long the endpoint takes to answer, and they give up after 20 milliseconds. Right now the handler does the write before it answers, so a burst of 400 readings ties the endpoint up for the better part of 17 seconds. There is already a background service in the app that was supposed to do this work, and today it does nothing useful.

The **1. Start** folder contains the intentionally imperfect code you will edit. The **2. Finish** folder contains the completed Blazor version.

## 1. Open the Starter Project

1. Using File Explorer on Windows or Finder on macOS, navigate to **BecomeAnExpertWithAsyncAwait/9. Channels/1. Start**.
2. Open **TelemetryPipeline.slnx** in your IDE.
3. Build the project once so you can confirm the starter compiles.
4. Run the project and open [http://localhost:5013](http://localhost:5013).

## 2. Inspect the Starting Code

1. Open **TelemetryPipeline/Services/TelemetryIngestService.cs** and find each `// ToDo Refactor` comment. This is the only file you need to change.
2. Open **TelemetryPipeline/Services/TelemetryProcessor.cs**. It is a `BackgroundService` that calls `DrainAsync` for the life of the app. This is the consumer half of the pipeline.
3. Open **TelemetryPipeline/Services/EventStore.cs** and notice that `SaveAsync` takes 40 milliseconds. That is the write you cannot make faster.
4. Open **TelemetryPipeline/Components/Pages/Ingest.razor.cs** and notice that the page runs a burst of 400 events and reads its counters straight off the singleton service.

Run the app and click **Receive 400 events**. Here is what you will see:

1. The button spins for about 16.56 seconds before any results appear.
2. **Accepted** reads 400 of 400. Nothing is lost, so the bug is not correctness. It is time.
3. **Average accept** reads about 41.4ms, and the card is red. A device gives up after 20ms, so in production every one of these readings becomes a retry.
4. **Burst duration** reads about 16.56s. Nothing is blocked. `SaveAsync` is an awaited delay, so no thread is held and other callers are still served. What that number measures is per-caller latency multiplied by the burst: every one of those 400 devices waited more than twice its 20ms timeout, so every one of them retries, and the retries land on an endpoint whose latency is already the sum of everything downstream of it.
5. **Written to store** already reads 400, with nothing meaningful waiting in the queue. You will see 0 or 1, because the write happened inside the accept call. The background service did none of it.

Pay attention to these clues:

1. `AcceptAsync` awaits `eventStore.SaveAsync(...)` before it returns. The device holds the connection open for the entire database write.
2. `_processed++` happens in the accept path, so "Written to store" is counting the same work as "Accepted". The consumer is decoration.
3. `_pending` is a `List<TelemetryEvent>` that the accept path writes while `DrainAsync` reads it. `List<T>` is not thread safe, so items can be lost and the list can throw.
4. `_accepted++` and `_processed++` are not atomic, and `TelemetryIngestService` is a singleton shared by every browser session.
5. `DrainAsync` polls. When the list is empty it sleeps for 25 milliseconds and looks again, forever, whether or not there is anything to do. This periodic polling adds latency and wakes the loop even when no work exists.
6. Nothing ever tells `DrainAsync` that the producer is finished. The only thing that stops it is cancellation at shutdown.
7. Nothing limits how large `_pending` can grow. A big enough burst is an out of memory exception.

## 3. Challenge: Drain the Pipeline

Recommended time: 20 minutes.

> **Note:** Please avoid letting AI Agents solve the challenges for you. You're smart. You got this. Use them to understand the existing code, clarify producer and consumer concepts, interpret errors, and ask questions that help you decide what to change. The goal is to practice the reasoning yourself.

Refactor **Services/TelemetryIngestService.cs** so that accepting an event only queues it, and the background service does the database writes.

Requirements:

1. Add a `using` for the namespace that holds `Channel<T>`.
2. Replace the `List<TelemetryEvent>` with a queue that is thread safe, has a capacity you choose, and can be awaited. Add a capacity constant large enough to hold the whole 400 event burst.
3. Decide what a full queue should do to the device that is writing, and pick the option that makes the producer wait instead of losing readings.
4. Be honest in the options about how many producers and consumers there really are. Many devices write, and you are about to run more than one consumer.
5. Change `AcceptAsync` so it only queues the event. Move the `EventStore.SaveAsync` call out of the accept path entirely.
6. Return the task type that allocates nothing when the call completes synchronously, because the queue write almost always does.
7. Make the counters safe for a producer and several consumers to touch at once, and make sure the Blazor page reads the latest value rather than one cached in a register.
8. Report `QueueDepth` from the queue itself instead of a list count.
9. Write a private consumer method that reads the queue until it is complete and saves each event to `EventStore`.
10. Change `DrainAsync` so it runs several consumers over the same reader and waits for all of them. Add a consumer count constant. 8 is a good starting point.
11. Add a public method that marks the queue complete. Do not call it from `RunBurstAsync`. Completing the queue closes it for good, and the next burst would throw `ChannelClosedException`. It belongs on the shutdown path, which is next.
12. Wire that method into **Services/TelemetryProcessor.cs** so shutting the app down drains the backlog instead of throwing it away. Override `StopAsync`, complete the queue before you call the base implementation, and stop passing `stoppingToken` into the read loop. Cancelling that loop is exactly what abandons the queued readings.
13. Delete the polling loop, the `Task.Delay` inside it, the `List<TelemetryEvent>`, the `Reset()` method, and the call to `Reset()` at the top of `RunBurstAsync`.

Acceptance checks. Restart the app, then click **Receive 400 events** once:

1. **TelemetryPipeline.slnx** builds.
2. **Accepted** reads 400 of 400.
3. **Average accept** reads 0.0ms, and the card is green instead of red.
4. **Burst duration** reads 0.00s. The button no longer spins.
5. **Written to store** starts near zero, with the rest of the burst waiting in the queue.
6. Click **Refresh counters** once a second. The written count climbs, the waiting count falls, and about two seconds after the burst it reads 400 written with 0 waiting.
7. Click **Receive 400 events** and then press Ctrl+C in the terminal straight away, while the queue still has items waiting. The app pauses for roughly two seconds before it exits, because it is finishing the backlog first. It shuts down instead of hanging, and it does not quit instantly either.
8. There is no `List<TelemetryEvent>` and no `Task.Delay` left in the service.
9. Your code is ready to compare with **2. Finish/TelemetryPipeline**.

The counters keep counting for the life of the app once the consumers are running, so restart the app before you measure a fresh burst.

If you finish early, drop the capacity constant from 500 to 50 and run the burst again. The burst no longer fits in the buffer, the write starts waiting for room, and the average accept time climbs off zero. That is backpressure, and no code in the app measured anything or decided to slow down. Then change the full queue behavior from waiting to dropping the oldest queued item, run it once more, and watch the written count settle below 400 with no exception and no log line.

## 4. Review the Solution

After you have attempted the challenge, pause here for group review.

We will compare approaches, talk through capacity, backpressure, and how many consumers is the right number, and answer questions before opening [SOLUTION.md](SOLUTION.md) together.

The solution walkthrough shows the order to make each change and points you to the completed code in **2. Finish/TelemetryPipeline**.
