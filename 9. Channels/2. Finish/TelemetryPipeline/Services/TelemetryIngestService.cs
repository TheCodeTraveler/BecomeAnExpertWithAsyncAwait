using System.Diagnostics;
using System.Threading.Channels;

namespace TelemetryPipeline;

// Receives telemetry from devices and gets it into the database.
// AcceptAsync stands in for a webhook endpoint: devices retry if it is slow.
public sealed class TelemetryIngestService(EventStore eventStore)
{
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

	int _accepted;
	int _processed;

	public int Accepted => Volatile.Read(ref _accepted);

	public int Processed => Volatile.Read(ref _processed);

	public int QueueDepth => _events.Reader.Count;

	// Returns as soon as the event is queued. The device is not waiting on the database.
	public async ValueTask AcceptAsync(TelemetryEvent telemetryEvent, CancellationToken token)
	{
		await _events.Writer.WriteAsync(telemetryEvent, token).ConfigureAwait(false);

		Interlocked.Increment(ref _accepted);
	}

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

	// Marks the channel complete so DrainAsync finishes after the backlog is written
	public void CompleteWriting() => _events.Writer.TryComplete();

	public async Task<PipelineStats> RunBurstAsync(int eventCount, CancellationToken token)
	{
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
			Accepted,
			Processed,
			0,
			QueueDepth,
			totalAcceptMilliseconds / eventCount,
			slowestAccept,
			stopwatch.Elapsed.TotalSeconds);
	}

	async Task ConsumeAsync(CancellationToken token)
	{
		await foreach (var telemetryEvent in _events.Reader.ReadAllAsync(token).ConfigureAwait(false))
		{
			await eventStore.SaveAsync(telemetryEvent, token).ConfigureAwait(false);

			Interlocked.Increment(ref _processed);
		}
	}
}