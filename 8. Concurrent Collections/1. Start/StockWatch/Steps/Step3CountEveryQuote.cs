using StockWatch.Components.Pages;

namespace StockWatch;

public sealed class Step3CountEveryQuote : WorkshopStep
{
	// Refreshes that overlap, the way timer ticks do when the feed is slow, so far more increments land at the same moment than on one tick
	const int _overlappingRefreshes = 32;

	const string _lostCountHint = "Quotes were applied and never counted, and nothing threw. _refreshCount++ is a read, an add, and a write, and two quotes that land at the same moment can both read the same count. "
		+ "Replace it with an atomic increment.";

	public override int Number => 3;

	public override string Scenario => "Count every quote";

	public override string Title => "RefreshQuotes() and RefreshCount";

	public override string Story => "The quotes applied tile is how the support desk tells a slow feed from a stalled one. It should read 60 on the first paint and climb by exactly 60 on every tick. "
		+ "_refreshCount++ runs once per quote, from whichever Parallel.ForEachAsync worker fetched it, and ++ is a read, an add, and a write. Two quotes that land at the same moment can both read the same count, and one increment disappears. "
		+ "One lost increment out of sixty, with nothing to tell you it happened, is the quietest bug on this page.";

	public override string SeeItInTheApp => "On the Dashboard page, read the quotes applied tile. The first paint should read 60, and every so often it reads 59 instead, more often on a machine with more cores. "
		+ "Wait 2 seconds: it should climb by exactly 60 on every tick, and every so often it climbs by 59.";

	public override string FileToChange => "Components/Pages/Dashboard.razor.cs";

	public override IReadOnlyList<string> Tasks { get; } =
	[
		"Replace _refreshCount++ in RefreshQuotes() with an atomic increment.",
		"Read _refreshCount in the RefreshCount getter in a way that forces a real read of the field, instead of a value the JIT cached in a register.",
	];

	public override IReadOnlyList<string> Clues { get; } =
	[
		"_refreshCount++ looks like one operation. It is three: read the field, add one, and write it back. Increments that interleave get lost.",
		"RefreshCount is read by Blazor's renderer on a different thread than the one that last wrote _refreshCount.",
		"An int does not need a lock. Interlocked updates one atomically, and Volatile reads one safely.",
	];

	public override string TimeoutHint => "RefreshQuotes() never returned. Check that nothing in it waits on a lock or a semaphore.";

	public override async Task Run(StepContext context)
	{
		var report = context.Report;
		var token = context.Token;

		if (!DashboardHarness.HasMembers(report, "RefreshQuotes", "StopRefreshTimer"))
			return;

		// Part 1: a fresh dashboard, the same page a new browser tab gets. Its first paint shows one quote applied per symbol.
		await using var harness = await DashboardHarness.Render().ConfigureAwait(false);

		if (!await harness.LoadAndStopTimer(report, token).ConfigureAwait(false))
			return;

		report.Expect("After loading, quotes applied reads 60", DashboardHarness.SymbolCount, harness.Dashboard.RefreshCount, _lostCountHint);

		// Part 2: overlapping refreshes, so many quotes land at the same moment
		var countBefore = harness.Dashboard.RefreshCount;
		var expectedCount = countBefore + (_overlappingRefreshes * DashboardHarness.SymbolCount);
		report.Log($"Running {_overlappingRefreshes} refreshes at the same time, {_overlappingRefreshes * DashboardHarness.SymbolCount:N0} quotes in all");

		var refreshes = Task.WhenAll(Enumerable.Range(0, _overlappingRefreshes).Select(_ => harness.RefreshQuotes(token)));
		var refreshOutcome = await DashboardHarness.Describe(refreshes, TimeSpan.FromSeconds(10), token).ConfigureAwait(false);

		report.Log($"The refreshes {refreshOutcome}, and quotes applied reads {harness.Dashboard.RefreshCount:N0}");

		if (refreshOutcome is not DashboardHarness.Finished)
		{
			report.Expect("Overlapping refreshes finish", DashboardHarness.Finished, refreshOutcome, false, "RefreshQuotes() has to finish every refresh, even when refreshes overlap. Check what the code in its loop can throw or wait on.");
			return;
		}

		report.Expect($"{_overlappingRefreshes} overlapping refreshes add exactly 60 each", expectedCount, harness.Dashboard.RefreshCount, _lostCountHint);

		// Part 3: a lost increment is timing dependent, so the refreshes above can come out right by luck. These read your compiled code instead.
		var refreshCalls = CodeInspector.GetCalledMethods(typeof(DashboardPageBase), "RefreshQuotes");
		var usesInterlocked = refreshCalls.Contains("Interlocked.Increment") || refreshCalls.Contains("Interlocked.Add");
		report.Log($"RefreshQuotes() and its lambdas call {refreshCalls.Count} methods{(usesInterlocked ? ", including Interlocked.Increment" : ", and none of them is on Interlocked")}");

		report.Expect(
			"RefreshQuotes() counts each quote with Interlocked",
			"Interlocked.Increment",
			usesInterlocked ? "Interlocked.Increment" : "no Interlocked call",
			usesInterlocked,
			"Lost increments depend on timing, so this checks your code instead of trusting one lucky run. _refreshCount++ is a read, an add, and a write. Replace it with the atomic increment from the Interlocked class.");

		var getterCalls = CodeInspector.GetCalledMethods(typeof(DashboardPageBase), "get_RefreshCount");
		var usesVolatileRead = getterCalls.Contains("Volatile.Read");

		report.Expect(
			"The RefreshCount getter reads _refreshCount with Volatile.Read",
			"Volatile.Read",
			usesVolatileRead ? "Volatile.Read" : "a plain field read",
			usesVolatileRead,
			"No test can reliably catch a stale read, so this checks your code. RefreshCount is read by Blazor's renderer on a different thread than the one that last wrote _refreshCount. Read the field in a way that forces a real read, instead of a value the JIT may have cached.");
	}
}