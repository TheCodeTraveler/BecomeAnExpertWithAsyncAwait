using System.Diagnostics;
using System.Threading.Channels;

namespace TelemetryPipeline;

public sealed class Step3DrainWithSeveralConsumers : WorkshopStep
{
	const int _burstSize = 80;
	const int _secondBurstSize = 10;

	const string _oneConsumerHint = "One consumer writing one reading at a time clears 25 readings per second, so 80 readings take 3.2 seconds. "
		+ "Write a consumer that reads the queue until it is complete, saves each reading to EventStore and counts it as processed. Then have DrainAsync run several of them over the same reader, and wait for all of them.";

	const string _returnedEarlyHint = "DrainAsync returned while readings were still waiting. A consumer has to keep reading until the queue is complete, not until it happens to be empty.";

	const string _countedTwiceHint = "More readings were counted as processed than were accepted. Count each reading once, after it reaches the store.";

	const string _cumulativeHint = "The consumers keep draining earlier bursts in the background, so the pipeline outlives any single burst and its counters are cumulative. "
		+ "Delete Reset() and the call to it at the top of RunBurstAsync.";

	const string _closedHint = "The second burst found the queue closed. Completing the queue closes it for good, so nothing that runs during a burst may complete it. That belongs on the shutdown path, in Step 4.";

	static readonly TimeSpan _idleTime = TimeSpan.FromMilliseconds(200);
	static readonly TimeSpan _drainDeadline = TimeSpan.FromSeconds(1.5);
	static readonly TimeSpan _drainPatience = TimeSpan.FromSeconds(5);
	static readonly TimeSpan _stopTimeout = TimeSpan.FromSeconds(2);
	static readonly TimeSpan _pollingInterval = TimeSpan.FromMilliseconds(20);

	public override int Number => 3;

	public override string Scenario => "Drain with several consumers";

	public override string Title => "DrainAsync() and its consumers";

	public override string Story => "Readings are queued now, and every device gets its answer right away. But the only thing reading that queue is a loop that polls. "
		+ "When the queue is empty it sleeps for 25 milliseconds and looks again, forever, whether or not there is anything to do. "
		+ "And one consumer writing at 40 milliseconds per reading clears 25 readings per second, so a 400 reading burst takes 16 seconds to reach the store. "
		+ "That is the latency you just took away from the devices, moved somewhere nobody is watching.";

	public override string SeeItInTheApp => "Restart the app, receive 400 events on the Ingest page, and click Refresh counters once a second. "
		+ "Written to store should climb while the waiting count falls, and reach 400 written with 0 waiting about two seconds after the burst. "
		+ "The counters keep counting for the life of the app, so restart it before you measure a fresh burst.";

	public override string FileToChange => "Services/TelemetryIngestService.cs";

	public override IReadOnlyList<string> Tasks { get; } =
	[
		"Write a private consumer method that reads the queue until it is complete and saves each reading to EventStore.",
		"Count each reading as processed once it reaches the store, safely, because several consumers update that counter at once.",
		"Change DrainAsync so it runs several consumers over the same reader and waits for all of them. Add a consumer count constant. 8 is a good starting point.",
		"Pass DrainAsync's CancellationToken on to every consumer, so cancelling it stops them.",
		"Delete the polling loop and the Task.Delay inside it.",
		"Delete Reset() and the call to Reset() at the top of RunBurstAsync. The consumers keep draining earlier bursts in the background, so the counters are cumulative now.",
	];

	public override IReadOnlyList<string> Clues { get; } =
	[
		"DrainAsync polls. When the queue is empty it sleeps for 25 milliseconds and looks again, forever, whether or not there is anything to do. This periodic polling adds latency and wakes the loop even when no work exists.",
		"A channel's reader can hand you every reading as an IAsyncEnumerable. It waits without holding a thread while the queue is empty, and it ends once the queue is complete and drained.",
		"One consumer at 40 milliseconds per write can only clear 25 readings per second. Several consumers can share one reader, and each reading is handed to exactly one of them.",
		"Task.WhenAll waits for several tasks at once.",
	];

	public override string TimeoutHint => "DrainAsync never handed control back. Check that it starts every consumer and awaits them, rather than spinning in a loop that never awaits.";

	public override async Task Run(StepContext context)
	{
		var report = context.Report;
		var token = context.Token;

		var ingest = new TelemetryIngestService(new EventStore());
		using var drainCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);

		// Part 1: TelemetryProcessor starts the consumers with the app, long before the first reading arrives
		report.Log("Starting DrainAsync on a fresh service with an empty queue");

		var drain = ingest.DrainAsync(drainCancellationTokenSource.Token);

		await Task.Delay(_idleTime, token).ConfigureAwait(false);

		// Rethrows anything DrainAsync threw
		if (drain.IsFaulted)
			await drain.ConfigureAwait(false);

		report.Expect(
			"DrainAsync keeps waiting while the queue is empty",
			"still running",
			drain.IsCompleted ? "returned" : "still running",
			"DrainAsync returned as soon as it found the queue empty. Consumers start with the app, before the first reading arrives, so they have to keep waiting for readings until the queue is complete.");

		// Part 2: one consumer at 40 milliseconds per write needs 3.2 seconds for 80 readings. Eight consumers need 0.4 seconds.
		var stopwatch = Stopwatch.StartNew();

		await ingest.RunBurstAsync(_burstSize, token).ConfigureAwait(false);
		report.Log($"Received a burst of {_burstSize} readings in {stopwatch.Elapsed.TotalSeconds:F2}s. Waiting for the consumers to write them to the store");

		await WaitForProcessed(ingest, drain, _burstSize, _drainPatience, token).ConfigureAwait(false);
		stopwatch.Stop();

		var processed = ingest.Processed;
		var elapsed = stopwatch.Elapsed;

		report.Log($"{processed} of {_burstSize} readings were written after {elapsed.TotalSeconds:F2}s, and QueueDepth reads {ingest.QueueDepth}{(drain.IsCompleted ? ". DrainAsync has returned" : string.Empty)}");

		report.Expect(
			$"All {_burstSize} readings are written within {_drainDeadline.TotalSeconds} seconds",
			$"{_burstSize} written within {_drainDeadline.TotalSeconds}s",
			$"{processed} written after {elapsed.TotalSeconds:F2}s",
			processed == _burstSize && elapsed <= _drainDeadline,
			processed > _burstSize ? _countedTwiceHint : drain.IsCompleted ? _returnedEarlyHint : _oneConsumerHint);

		report.Expect("QueueDepth is back to 0", 0, ingest.QueueDepth, "Readings are still waiting in the queue. Every consumer has to keep reading until the queue is complete, not until it happens to be empty.");

		// Part 3: the pipeline outlives any single burst, so a second burst arrives while the consumers are still running
		var expectedTotal = _burstSize + _secondBurstSize;
		var secondBurstHint = _cumulativeHint;
		string secondBurst;

		report.Log($"Receiving a second burst of {_secondBurstSize} readings on the same service");

		try
		{
			await ingest.RunBurstAsync(_secondBurstSize, token).ConfigureAwait(false);
			await WaitForProcessed(ingest, drain, expectedTotal, _stopTimeout, token).ConfigureAwait(false);

			secondBurst = $"{ingest.Accepted} accepted, {ingest.Processed} written";
		}
		catch (ChannelClosedException)
		{
			secondBurst = $"threw {nameof(ChannelClosedException)}";
			secondBurstHint = _closedHint;
		}

		report.Log($"After the second burst: {secondBurst}");
		report.Expect("A second burst adds to the counters while the consumers keep running", $"{expectedTotal} accepted, {expectedTotal} written", secondBurst, secondBurstHint);

		// Part 4: polling and a lost increment are hard to see from outside, so the code is checked too
		var calls = CodeInspector.GetCalledMethods(typeof(TelemetryIngestService));
		var delays = calls.Count(static call => call is "Task.Delay");

		report.Expect(
			"No Task.Delay is left in TelemetryIngestService",
			"none",
			delays is 0 ? "none" : $"{delays} calls",
			"Waiting for the next reading should be an await on the queue, not a sleep in a loop. Delete the polling loop and the Task.Delay inside it.");

		var acceptAsync = typeof(TelemetryIngestService).GetMethod(nameof(TelemetryIngestService.AcceptAsync), [typeof(TelemetryEvent), typeof(CancellationToken)]);
		var acceptCounterUpdates = acceptAsync is null ? 0 : CodeInspector.GetCalledMethods(acceptAsync).Count(IsCounterUpdate);
		var consumerCounterUpdates = calls.Count(IsCounterUpdate) - acceptCounterUpdates;

		report.Expect(
			"The consumers count processed readings with Interlocked",
			"Interlocked",
			consumerCounterUpdates > 0 ? "Interlocked" : "no Interlocked call outside AcceptAsync",
			"_processed++ is a read, an add, and a write, and several consumers update it at once. Update the counter atomically.");

		// Part 5: cancelling DrainAsync's token has to stop every consumer
		report.Log("Cancelling DrainAsync's CancellationToken");
		await drainCancellationTokenSource.CancelAsync().ConfigureAwait(false);

		string stopped;

		try
		{
			await drain.WaitAsync(_stopTimeout, token).ConfigureAwait(false);
			stopped = "stopped";
		}
		catch (OperationCanceledException) when (drainCancellationTokenSource.IsCancellationRequested && !token.IsCancellationRequested)
		{
			stopped = "stopped";
		}
		catch (TimeoutException)
		{
			stopped = $"still running after {_stopTimeout.TotalSeconds} seconds";
		}

		report.Log($"DrainAsync: {stopped}");
		report.Expect("DrainAsync stops when its CancellationToken is cancelled", "stopped", stopped, "Pass the CancellationToken DrainAsync receives on to every consumer, and from there to the read and to the write to the store.");
	}

	static bool IsCounterUpdate(string call) => call is "Interlocked.Increment" or "Interlocked.Add";

	// Checks the counter the way the Ingest page's Refresh counters button does, until it reaches the target, DrainAsync returns, or patience runs out
	static async Task WaitForProcessed(TelemetryIngestService ingest, Task drain, int target, TimeSpan patience, CancellationToken token)
	{
		var stopwatch = Stopwatch.StartNew();

		while (ingest.Processed < target && !drain.IsCompleted && stopwatch.Elapsed < patience)
		{
			await Task.Delay(_pollingInterval, token).ConfigureAwait(false);
		}
	}
}