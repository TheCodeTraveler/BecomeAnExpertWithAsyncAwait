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