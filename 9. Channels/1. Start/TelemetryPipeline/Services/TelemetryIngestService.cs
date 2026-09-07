using System.Diagnostics;

namespace TelemetryPipeline;

// Receives telemetry from devices and gets it into the database.
// AcceptAsync stands in for a webhook endpoint: devices retry if it is slow.
public sealed class TelemetryIngestService(EventStore eventStore)
{
	// ToDo Refactor: a plain List is not thread safe. The accept path writes to it
	// while the drain loop reads from it, so events are lost or this throws.
	readonly List<TelemetryEvent> _pending = [];

	int _accepted;
	int _processed;

	public int Accepted => _accepted;

	public int Processed => _processed;

	public int QueueDepth => _pending.Count;

	public async Task AcceptAsync(TelemetryEvent telemetryEvent, CancellationToken token)
	{
		// ToDo Refactor: the device is waiting on this call while we write to the
		// database. A webhook should accept the event and return immediately.
		await eventStore.SaveAsync(telemetryEvent, token).ConfigureAwait(false);

		_pending.Add(telemetryEvent);
		_accepted++;
		_processed++;
	}

	// ToDo Refactor: this loop spins whether or not there is anything to do,
	// and nothing tells it when the pipeline has been shut down.
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

	public async Task<PipelineStats> RunBurstAsync(int eventCount, CancellationToken token)
	{
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

	void Reset()
	{
		_pending.Clear();
		_accepted = 0;
		_processed = 0;
	}
}