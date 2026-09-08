using System.Diagnostics;

namespace OrderPortal;

public sealed class CheckoutService(OrderMetrics metrics, TaxRateProvider taxRates, InventoryLedger ledger) : IDisposable
{
	static readonly string[] _regions = ["US-CA", "US-NY", "US-TX", "US-WA"];
	static readonly string[] _skus = ["SKU-1000", "SKU-2000", "SKU-3000"];

	readonly SemaphoreSlim _burstSemaphore = new(1, 1);

	public void Dispose() => _burstSemaphore.Dispose();

	// Runs `orderCount` checkouts with bounded concurrency, the way a burst of real
	// traffic would. Every checkout touches the same singleton services.
	public async Task<CheckoutResult> RunCheckoutBurstAsync(int orderCount, CancellationToken token)
	{
		await _burstSemaphore.WaitAsync(token).ConfigureAwait(false);

		try
		{
			metrics.Reset();
			taxRates.Reset();

			var stopwatch = Stopwatch.StartNew();

			await Parallel.ForEachAsync(
				Enumerable.Range(0, orderCount),
				token,
				async (orderNumber, cancellationToken) => await PlaceOrderAsync(orderNumber, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

			stopwatch.Stop();

			return new CheckoutResult(metrics.OrdersPlaced, metrics.Revenue, taxRates.Lookups, taxRates.Builds, stopwatch.Elapsed);
		}
		finally
		{
			_burstSemaphore.Release();
		}
	}

	async Task PlaceOrderAsync(int orderNumber, CancellationToken token)
	{
		var region = _regions[orderNumber % _regions.Length];

		var subtotal = 20m + orderNumber % 80;
		var total = subtotal * (1 + taxRates.GetRate(region));

		// Pretend this is the payment gateway
		await Task.Delay(TimeSpan.FromMilliseconds(2), token).ConfigureAwait(false);

		metrics.RecordOrder(total);
	}

	// Reserving stock is a separate step so the deadlock can be demonstrated on its own
	public Task<bool> ReserveStockAsync(int orderNumber, CancellationToken token)
	{
		var sku = _skus[orderNumber % _skus.Length];

		return ledger.ReserveStockAsync(sku, 1, token);
	}
}