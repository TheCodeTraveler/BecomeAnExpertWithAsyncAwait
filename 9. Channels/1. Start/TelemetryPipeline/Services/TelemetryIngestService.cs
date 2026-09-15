using System.Diagnostics;

namespace TelemetryPipeline;

// Receives telemetry from devices and gets it into the database.
// AcceptAsync stands in for a webhook endpoint: devices retry if it is slow.
public sealed class TelemetryIngestService(EventStore eventStore)
{
	// ToDo Refactor (Step 2): a plain List is not thread safe. The accept path writes to it
	// while the drain loop reads from it, so events are lost or this throws.
	// Nothing limits how large it can grow, and nothing can wait on it.
	readonly List<TelemetryEvent> _pending = [];

	int _accepted;
	int _processed;

	// ToDo Refactor (Step 2): a producer and several consumers are about to touch these counters at once,
	// and an int read can be stale. The page should read the latest value, not one cached in a register.
	public int Accepted => _accepted;

	// ToDo Refactor (Step 2): an int read can be stale
	public int Processed => _processed;

	// ToDo Refactor (Step 2): report the depth of the queue itself
	public int QueueDepth => _pending.Count;

	// ToDo Refactor (Step 1): queuing an event almost always completes synchronously, and Task allocates
	// on every call. Return the task type that allocates nothing when the call completes synchronously.
	public async Task AcceptAsync(TelemetryEvent telemetryEvent, CancellationToken token)
	{
		// ToDo Refactor (Step 1): the device is waiting on this call while we write to the
		// database. A webhook should accept the event and return immediately.
		await eventStore.SaveAsync(telemetryEvent, token).ConfigureAwait(false);

		_pending.Add(telemetryEvent);

		// ToDo Refactor (Step 2): `++` is a read, an add, and a write, and many devices post at once
		_accepted++;

		// ToDo Refactor (Step 1): this counts the event as written to the store before any consumer
		// has written it, so "Written to store" counts the same work as "Accepted"
		_processed++;
	}

	// ToDo Refactor (Step 3): this loop spins whether or not there is anything to do,
	// and it removes events without ever writing them to the store.
	// ToDo Refactor (Step 4): nothing tells it when the pipeline has been shut down.
	public async Task DrainAsync(CancellationToken token)
	{
		while (!token.IsCancellationRequested)
		{
			if (_pending.Count is 0)
			{
				await Task.Delay(TimeSpan.FromMilliseconds(25), token).ConfigureAwait(false);
				continue;
			}

			_pending.RemoveAt(0);
		}
	}

	// ToDo Refactor (Step 4): nothing tells the consumers that no more events are coming
	public void CompleteWriting()
	{
		throw new NotImplementedException("TelemetryIngestService.CompleteWriting() is not implemented yet. Hint: mark the queue complete, so the consumers finish writing the backlog and then stop reading. Do not call it from RunBurstAsync: a completed queue is closed for good, and the next burst would throw. It belongs on the shutdown path, in TelemetryProcessor.");
	}

	public async Task<PipelineStats> RunBurstAsync(int eventCount, CancellationToken token)
	{
		// ToDo Refactor (Step 3): the consumers keep draining earlier bursts in the background,
		// so clearing the counters here makes them wrong
		Reset();

		var stopwatch = Stopwatch.StartNew();
		var totalAcceptMilliseconds = 0d;
		var slowestAccept = 0d;

		for (var eventId = 0; eventId < eventCount; eventId++)
		{
			var acceptStopwatch = Stopwatch.StartNew();

			await AcceptAsync(
				new TelemetryEvent(eventId, $"device-{eventId % 20}", eventId * 0.37, DateTimeOffset.UtcNow),
				token).ConfigureAwait(false);

			acceptStopwatch.Stop();

			totalAcceptMilliseconds += acceptStopwatch.Elapsed.TotalMilliseconds;
			slowestAccept = Math.Max(slowestAccept, acceptStopwatch.Elapsed.TotalMilliseconds);
		}

		stopwatch.Stop();

		return new PipelineStats(
			_accepted,
			_processed,
			0,
			_pending.Count,
			totalAcceptMilliseconds / eventCount,
			slowestAccept,
			stopwatch.Elapsed.TotalSeconds);
	}

	// ToDo Refactor (Step 2): this clears the list
	// ToDo Refactor (Step 3): and it resets counters that the consumers are still updating
	void Reset()
	{
		_pending.Clear();
		_accepted = 0;
		_processed = 0;
	}
}