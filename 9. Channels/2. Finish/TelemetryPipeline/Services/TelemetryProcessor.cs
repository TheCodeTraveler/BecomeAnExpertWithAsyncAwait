namespace TelemetryPipeline;

// The consumer half of the pipeline. A BackgroundService is the standard place
// to run a channel reader for the lifetime of the application.
public sealed class TelemetryProcessor(TelemetryIngestService ingest) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			await ingest.DrainAsync(stoppingToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			// Expected on shutdown
		}
	}
}