namespace TelemetryPipeline;

// The consumer half of the pipeline.
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