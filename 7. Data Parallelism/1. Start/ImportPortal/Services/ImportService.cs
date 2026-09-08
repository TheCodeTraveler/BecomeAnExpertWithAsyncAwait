using System.Diagnostics;

namespace ImportPortal;

public sealed class ImportService(OrderFileService orderFile, CustomerApiService customerApi)
{
	public async Task<ImportReport> RunImportAsync(int rowCount, CancellationToken token)
	{
		var orders = orderFile.ReadOrders(rowCount);

		// Step 1: validate every row. This is CPU-bound work.
		var validateStopwatch = Stopwatch.StartNew();

		// ToDo Refactor: every row is scored on one thread while the other
		// cores sit idle. Nothing here depends on the row before it.
		foreach (var order in orders)
		{
			order.RiskScore = ScoreRisk(order);
		}

		validateStopwatch.Stop();

		// Step 2: enrich every row from the customer API. This is I/O-bound work.
		var enrichStopwatch = Stopwatch.StartNew();

		// ToDo Refactor: Parallel.ForEach takes an Action, not a Func<Task>.
		// This lambda is `async void`: ForEach starts each one and immediately
		// considers it finished, so this returns long before any call completes
		// and any exception inside it is rethrown where nothing can catch it.
		Parallel.ForEach(orders, async order =>
		{
			order.CustomerTier = await customerApi.GetCustomerTierAsync(order.Sku, token).ConfigureAwait(false);
		});

		enrichStopwatch.Stop();

		// Step 3: summarize by region.
		var reportStopwatch = Stopwatch.StartNew();

		// ToDo Refactor: this query runs on one thread
		var regionTotals = orders
			.GroupBy(static order => order.Region)
			.Select(static group => new RegionTotal(
				group.Key,
				group.Count(),
				group.Sum(static order => order.Amount),
				(long)group.Average(static order => order.RiskScore)))
			.OrderBy(static total => total.Region)
			.ToList();

		reportStopwatch.Stop();

		await Task.CompletedTask.ConfigureAwait(false);

		return new ImportReport(
			orders.Count(static order => order.RiskScore > 0),
			orders.Count(static order => order.CustomerTier is not null),
			validateStopwatch.Elapsed,
			enrichStopwatch.Elapsed,
			reportStopwatch.Elapsed,
			regionTotals);
	}

	// Deliberately expensive: a real validation pass hashes, parses and checks rules
	static long ScoreRisk(OrderRow order)
	{
		long score = order.RowNumber;

		for (var iteration = 0; iteration < 150_000; iteration++)
		{
			score = (score * 31 + order.Sku.Length + iteration) % 1_000_003;
		}

		return score;
	}
}