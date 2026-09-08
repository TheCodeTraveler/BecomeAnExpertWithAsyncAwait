using Microsoft.AspNetCore.Components;

namespace ImportPortal.Components.Pages;

public partial class ImportPageBase : ComponentBase
{
	public const int RowCount = 4_000;

	[Inject]
	public required ImportService ImportService { get; init; }

	public bool IsBusy { get; private set; }

	public ImportReport? Report { get; private set; }

	protected async Task RunImportAsync()
	{
		IsBusy = true;
		Report = null;

		await InvokeAsync(StateHasChanged).ConfigureAwait(false);

		ImportReport? report = null;

		try
		{
			report = await ImportService.RunImportAsync(RowCount, CancellationToken.None).ConfigureAwait(false);
		}
		finally
		{
			// The continuation is off Blazor's renderer, so every component
			// state change goes back through it
			await InvokeAsync(() =>
			{
				Report = report;
				IsBusy = false;

				StateHasChanged();
			}).ConfigureAwait(false);
		}
	}
}