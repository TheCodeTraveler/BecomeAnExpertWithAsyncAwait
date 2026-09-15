namespace ImportPortal;

public sealed class Step3SummarizeAndCancel : WorkshopStep
{
	// Enough rows that scoring them takes a clearly measurable amount of CPU time
	const int _rowCount = 1_000;

	// A cancelled import that stops before validation uses almost no CPU time. One that scores every row first uses about as much as a full import.
	const double _maximumCancelledShare = 0.25;

	const string _summaryHint = "The regional summary no longer matches the uploaded file. Moving a query to PLINQ should not change its body at all: group by region, count, sum and average, then sort by region.";

	const string _plinqHint = "The regional summary still runs as a single-threaded LINQ query, or it ignores the token. Move the query to PLINQ, and make it honor the CancellationToken that RunImportAsync receives.";

	const string _ranToTheEndHint = "A cancelled import ran to the end and returned a report nobody wanted. Every stage has to honor the token RunImportAsync receives: "
		+ "set it on the ParallelOptions of each Parallel loop, hand it to the PLINQ query, and let the OperationCanceledException reach the caller.";

	const string _scoredRowsFirstHint = "The import did not notice the cancellation until it had scored every row. A foreach, or a Parallel loop whose ParallelOptions has no CancellationToken, never looks at the token. "
		+ "Give the validation stage's ParallelOptions the token RunImportAsync receives, and the loop checks it before it scores a single row.";

	const string _asyncVoidHint = "ImportService still has an async void lambda, and a cancelled token reaching one would throw where nothing can catch it and end the whole app, so this step did not cancel an import. "
		+ "Run Step 2 again and fix what it reports first.";

	public override int Number => 3;

	public override string Scenario => "Summarize with PLINQ, and stop when cancelled";

	public override string Title => "RunImportAsync(), the summary stage and its token";

	public override string Story => "When a bad file is uploaded, operations cancels the nightly import and expects it to stop. "
		+ "RunImportAsync has received a CancellationToken all along, but only the customer API call ever looked at it, so a cancelled import still scores every row before anything notices. "
		+ "Meanwhile the regional summary is the last stage still running on one thread, and it has to honor the same token.";

	public override string SeeItInTheApp => "The Import page has no Cancel button, so this step cancels an import for you. On the page, the Report card shows how long the regional grouping took on one thread, "
		+ "and Regional summary shows US-CA, US-NY, US-TX and US-WA with 1,000 orders each. None of those orders or revenue figures may change when the query moves to PLINQ.";

	public override string FileToChange => "Services/ImportService.cs";

	public override IReadOnlyList<string> Tasks { get; } =
	[
		"Turn the regional summary into a PLINQ query that honors the cancellation token.",
		"Keep the query body the same, so the report still shows four regions with the same order counts and the same revenue.",
		"Set CancellationToken through ParallelOptions on every Parallel loop, using the token RunImportAsync already receives, so a cancelled import stops before it scores a single row.",
		"Let the OperationCanceledException reach the caller. A cancelled import has no report to return.",
	];

	public override IReadOnlyList<string> Clues { get; } =
	[
		"The regional summary runs as a single-threaded LINQ query after every row is already in memory.",
		"The method already receives a CancellationToken, and nothing except the customer API call ever uses it.",
		"One extension method moves a LINQ query onto PLINQ, and another makes the query honor a token. ParallelOptions has a CancellationToken property too, and a Parallel loop checks it before it starts a single iteration.",
	];

	public override string TimeoutHint => "The import never finished. Check that the regional summary query ends, and that a cancelled import stops instead of waiting.";

	public override async Task Run(StepContext context)
	{
		var report = context.Report;

		// Part 1: a full import, and the regional summary it reports, compared with totals worked out straight from the same file
		var importService = new ImportService(new OrderFileService(), new CustomerApiService());

		report.Log($"Importing {_rowCount:N0} rows through a fresh ImportService");

		var processorTimeBefore = GetProcessorTime();
		var importReport = await importService.RunImportAsync(_rowCount, CancellationToken.None).ConfigureAwait(false);
		var fullImportProcessorTime = GetProcessorTime() - processorTimeBefore;

		report.Log($"The import used {fullImportProcessorTime.TotalMilliseconds:N0} ms of CPU time, and the summary took {importReport.ReportElapsed.TotalMilliseconds:N1} ms");

		var expectedTotals = new OrderFileService().ReadOrders(_rowCount)
			.GroupBy(static order => order.Region)
			.OrderBy(static group => group.Key, StringComparer.Ordinal)
			.Select(static group => new RegionTotal(group.Key, group.Count(), group.Sum(static order => order.Amount), AverageRisk: 0))
			.ToList();

		report.Expect(
			$"The summary has {expectedTotals.Count} regions with {_rowCount / expectedTotals.Count:N0} orders each",
			FormatOrders(expectedTotals),
			FormatOrders(importReport.RegionTotals),
			FormatOrders(expectedTotals) == FormatOrders(importReport.RegionTotals),
			_summaryHint);

		report.Expect(
			"The revenue for every region matches the uploaded file",
			FormatRevenue(expectedTotals),
			FormatRevenue(importReport.RegionTotals),
			FormatRevenue(expectedTotals) == FormatRevenue(importReport.RegionTotals),
			_summaryHint);

		// Part 2: the summary takes about a millisecond, far too little to measure, so this reads ImportService's compiled code instead
		var calledMethods = CodeInspector.GetCalledMethods(typeof(ImportService));
		string[] plinqMethods = ["AsParallel", "WithCancellation"];
		var calledPlinqMethods = plinqMethods.Where(name => calledMethods.Contains($"ParallelEnumerable.{name}")).Select(static name => $"{name}()").ToList();

		report.Expect(
			"The regional summary is a PLINQ query that honors the token",
			"AsParallel(), WithCancellation()",
			calledPlinqMethods.Count is 0 ? "neither" : string.Join(", ", calledPlinqMethods),
			calledPlinqMethods.Count == plinqMethods.Length,
			_plinqHint);

		// Part 3: cancel an import. Only safe once no async void lambda is left: a cancelled token reaching one would end the whole app.
		var asyncVoidMethods = CodeInspector.FindAsyncVoidMethods(typeof(ImportService));

		if (asyncVoidMethods.Count > 0)
		{
			report.Log($"ImportService has {asyncVoidMethods.Count} async void lambdas or methods, so this step will not cancel an import");
			report.Expect("A cancelled import throws OperationCanceledException", nameof(OperationCanceledException), "not run: ImportService has async void code", false, _asyncVoidHint);
			report.Expect("A cancelled import stops before it scores rows", $"under {_maximumCancelledShare:P0} of a full import's CPU time", "not run: ImportService has async void code", false, _asyncVoidHint);
			return;
		}

		// Cancelled before it starts, the way operations cancels a job the moment a bad file is spotted
		var cancelledToken = new CancellationToken(canceled: true);
		string? thrownException = null;

		report.Log($"Importing {_rowCount:N0} rows with a CancellationToken that is already cancelled");

		processorTimeBefore = GetProcessorTime();

		try
		{
			await importService.RunImportAsync(_rowCount, cancelledToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException e) when (cancelledToken.IsCancellationRequested)
		{
			thrownException = e.GetType().Name;
		}

		var cancelledProcessorTime = GetProcessorTime() - processorTimeBefore;
		var cancelledShare = cancelledProcessorTime / fullImportProcessorTime;

		report.Log($"The cancelled import {(thrownException is null ? "returned a report" : $"threw {thrownException}")} after {cancelledProcessorTime.TotalMilliseconds:N0} ms of CPU time, {cancelledShare:P0} of what the full import used");

		// TaskCanceledException derives from OperationCanceledException, and either one tells the caller the import was cancelled
		var outcome = thrownException switch
		{
			null => "returned a report",
			nameof(OperationCanceledException) => thrownException,
			_ => $"{thrownException}, an OperationCanceledException",
		};

		report.Expect("A cancelled import throws OperationCanceledException", nameof(OperationCanceledException), outcome, thrownException is not null, _ranToTheEndHint);

		report.Expect(
			"A cancelled import stops before it scores rows",
			$"under {_maximumCancelledShare:P0} of a full import's CPU time",
			$"{cancelledShare:P0} ({cancelledProcessorTime.TotalMilliseconds:N0} ms)",
			cancelledShare < _maximumCancelledShare,
			_scoredRowsFirstHint);
	}

	static string FormatOrders(IEnumerable<RegionTotal> totals) => string.Join(", ", totals.Select(static total => $"{total.Region} {total.Orders:N0}"));

	static string FormatRevenue(IEnumerable<RegionTotal> totals) => string.Join(", ", totals.Select(static total => $"{total.Region} {total.Revenue:C0}"));
}