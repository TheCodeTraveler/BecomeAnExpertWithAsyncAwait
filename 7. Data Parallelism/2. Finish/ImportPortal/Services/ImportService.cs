using System.Diagnostics;

namespace ImportPortal;

public sealed class ImportService(OrderFileService orderFile, CustomerApiService customerApi)
{
	public async Task<ImportReport> RunImportAsync(int rowCount, CancellationToken token)
	{
		var orders = orderFile.ReadOrders(rowCount);

		var parallelOptions = new ParallelOptions
		{
			CancellationToken = token,
			MaxDegreeOfParallelism = Environment.ProcessorCount,
		};

		// Step 1: validate every row. CPU-bound, so use every core.
		var validateStopwatch = Stopwatch.StartNew();

		Parallel.ForEach(orders, parallelOptions, static order => order.RiskScore = ScoreRisk(order));

		validateStopwatch.Stop();

		// Step 2: enrich every row from the customer API. I/O-bound, so use the
		// asynchronous overload. It awaits every call and bounds how many run at
		// once, which keeps the downstream API from being flooded.
		var enrichStopwatch = Stopwatch.StartNew();

		var enrichOptions = new ParallelOptions
		{
			CancellationToken = token,
			MaxDegreeOfParallelism = 32,
		};

		await Parallel.ForEachAsync(
			orders,
			enrichOptions,
			async (order, cancellationToken) =>
				order.CustomerTier = await customerApi.GetCustomerTierAsync(order.Sku, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

		enrichStopwatch.Stop();

		// Step 3: summarize by region. PLINQ is the declarative sibling of
		// Parallel.ForEach: same idea, expressed as a query.
		var reportStopwatch = Stopwatch.StartNew();

		var regionTotals = orders
			.AsParallel()
			.WithCancellation(token)
			.WithDegreeOfParallelism(Environment.ProcessorCount)
			.GroupBy(static order => order.Region)
			.Select(static group => new RegionTotal(
				group.Key,
				group.Count(),
				group.Sum(static order => order.Amount),
				(long)group.Average(static order => order.RiskScore)))
			.OrderBy(static total => total.Region)
			.ToList();

		reportStopwatch.Stop();

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