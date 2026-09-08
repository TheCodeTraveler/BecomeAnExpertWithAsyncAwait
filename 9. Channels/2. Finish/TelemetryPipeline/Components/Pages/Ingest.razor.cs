using Microsoft.AspNetCore.Components;

namespace TelemetryPipeline.Components.Pages;

public partial class IngestPageBase : ComponentBase
{
	public const int EventCount = 400;

	[Inject]
	public required TelemetryIngestService Ingest { get; init; }

	public bool IsBusy { get; private set; }

	public PipelineStats? Stats { get; private set; }

	public int LiveProcessed => Ingest.Processed;

	public int LiveQueueDepth => Ingest.QueueDepth;

	protected async Task RunBurstAsync()
	{
		IsBusy = true;
		Stats = null;

		await InvokeAsync(StateHasChanged).ConfigureAwait(false);

		PipelineStats? stats = null;

		try
		{
			stats = await Ingest.RunBurstAsync(EventCount, CancellationToken.None).ConfigureAwait(false);
		}
		finally
		{
			// The continuation is off Blazor's renderer, so every component
			// state change goes back through it
			await InvokeAsync(() =>
			{
				Stats = stats;
				IsBusy = false;

				StateHasChanged();
			}).ConfigureAwait(false);
		}
	}

	protected async Task RefreshAsync() => await InvokeAsync(StateHasChanged).ConfigureAwait(false);
}