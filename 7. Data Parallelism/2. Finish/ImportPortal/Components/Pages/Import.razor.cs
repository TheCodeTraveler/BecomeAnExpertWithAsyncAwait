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

		try
		{
			Report = await ImportService.RunImportAsync(RowCount, CancellationToken.None).ConfigureAwait(false);
		}
		finally
		{
			IsBusy = false;
			await InvokeAsync(StateHasChanged).ConfigureAwait(false);
		}
	}
}