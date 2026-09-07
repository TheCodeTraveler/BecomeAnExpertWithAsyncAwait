namespace TelemetryPipeline;

// Stands in for the database each event is written to. Writing is slow,
// which is exactly why it does not belong in the request handler.
public sealed class EventStore
{
	public async Task SaveAsync(TelemetryEvent telemetryEvent, CancellationToken token)
	{
		await Task.Delay(TimeSpan.FromMilliseconds(40), token).ConfigureAwait(false);
	}
}