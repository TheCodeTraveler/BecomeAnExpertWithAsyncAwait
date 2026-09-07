using Microsoft.AspNetCore.Components;

namespace OrderPortal.Components.Pages;

public partial class CheckoutPageBase : ComponentBase
{
	public const int OrderCount = 2_000;

	[Inject]
	public required CheckoutService CheckoutService { get; init; }

	public bool IsBusy { get; private set; }

	public CheckoutResult? Result { get; private set; }

	public string? StockMessage { get; private set; }

	// Every order is subtotal * (1 + rate) where subtotal is 20 + (orderNumber % 80)
	public static decimal ExpectedRevenue { get; } = CalculateExpectedRevenue();

	protected async Task RunBurstAsync()
	{
		IsBusy = true;
		Result = null;

		try
		{
			Result = await CheckoutService.RunCheckoutBurstAsync(OrderCount, CancellationToken.None).ConfigureAwait(false);
		}
		finally
		{
			IsBusy = false;
			await InvokeAsync(StateHasChanged).ConfigureAwait(false);
		}
	}

	protected async Task ReserveStockAsync()
	{
		IsBusy = true;
		StockMessage = "Reserving stock...";

		await InvokeAsync(StateHasChanged).ConfigureAwait(false);

		// A real request would eventually be abandoned by the client. This token
		// plays that role so a deadlock does not wedge the app forever.
		using var timeoutCancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));

		try
		{
			var reserved = await CheckoutService.ReserveStockAsync(0, timeoutCancellationTokenSource.Token).ConfigureAwait(false);

			StockMessage = reserved
				? "Reserved 1 of SKU-1000 and wrote the audit entry."
				: "Could not reserve stock: not enough on hand.";
		}
		catch (OperationCanceledException)
		{
			StockMessage = "Timed out after 5 seconds waiting for the ledger lock.\n"
				+ "No thread is blocked, because WaitAsync is awaited. The permit is simply never released.\n"
				+ "Look at InventoryLedger.ReserveStockAsync and ask which lock it is waiting for.";
		}
		finally
		{
			IsBusy = false;
			await InvokeAsync(StateHasChanged).ConfigureAwait(false);
		}
	}

	static decimal CalculateExpectedRevenue()
	{
		decimal[] rates = [0.0925m, 0.08875m, 0.0825m, 0.101m];
		decimal expected = 0;

		for (var orderNumber = 0; orderNumber < OrderCount; orderNumber++)
		{
			var subtotal = 20m + orderNumber % 80;
			expected += subtotal * (1 + rates[orderNumber % rates.Length]);
		}

		return expected;
	}
}