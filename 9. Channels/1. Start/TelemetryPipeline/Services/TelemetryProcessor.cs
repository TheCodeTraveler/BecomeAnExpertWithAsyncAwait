namespace TelemetryPipeline;

// The consumer half of the pipeline.
// ToDo Refactor (Step 4): when the app shuts down, nothing completes the queue first,
// so the readings still waiting in it never reach the store.
public sealed class TelemetryProcessor(TelemetryIngestService ingest) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			// ToDo Refactor (Step 4): the host cancels stoppingToken at shutdown, and cancelling
			// the read loop abandons every reading that is still waiting in the queue
			await ingest.DrainAsync(stoppingToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			// Expected on shutdown
		}
	}
}