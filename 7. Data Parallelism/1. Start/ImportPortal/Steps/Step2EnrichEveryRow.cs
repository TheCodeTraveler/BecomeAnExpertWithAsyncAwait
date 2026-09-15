namespace ImportPortal;

public sealed class Step2EnrichEveryRow : WorkshopStep
{
	// Small enough that even awaiting every call one at a time finishes well inside the step timeout
	const int _rowCount = 400;

	const string _notAwaitedHint = "RunImportAsync returned before the customer API answered. An async lambda passed to Parallel.ForEach is async void, "
		+ "so the loop counts each row as done the moment its call starts. Use the Parallel overload that is built for asynchronous bodies, and await it.";

	const string _inFlightHint = "Customer API calls were still running after RunImportAsync returned, so the import reported on work that had not finished. "
		+ "The enrichment stage has to be awaited before the method moves on.";

	const string _oneAtATimeHint = "Only one customer API call ran at a time, so the import makes every round trip back to back. Let the calls run in parallel, up to a limit you choose.";

	const string _unboundedHint = "Every row's call was in flight at the same moment. Against a real customer API, that is how a nightly job takes down a service other teams depend on. "
		+ "Give the enrichment stage its own ParallelOptions, with a MaxDegreeOfParallelism well below the row count.";

	const string _forEachAsyncHint = "The enrichment stage needs the Parallel method whose body receives a CancellationToken and returns a ValueTask. It awaits every body, and it bounds how many run at once.";

	const string _asyncVoidHint = "An async lambda passed where an Action is expected compiles to async void. Nothing can await it, and an exception inside it ends the whole app. "
		+ "Every async body in ImportService has to return a Task or a ValueTask.";

	public override int Number => 2;

	public override string Scenario => "Enrich every row, and wait for it";

	public override string Title => "RunImportAsync(), the enrichment stage";

	public override string Story => "Every row is supposed to come back with the customer's tier from the internal customer API. "
		+ "Last week someone noticed that not one row has a tier, and that the import has reported success every night for a year. "
		+ "The stage that should be waiting on thousands of 10 millisecond calls finishes in a hundredth of a second. "
		+ "And the day the customer API answers one of those calls with an error, the exception is thrown where nothing can catch it, and it takes the whole process down.";

	public override string SeeItInTheApp => "On the Import page, press Run import. The Enriched card reads 0 of 4,000 in a hundredth of a second or less, and it is red. "
		+ "4,000 calls at 10 milliseconds each cannot finish in a hundredth of a second, and they did not: the import stopped waiting for them before a single one answered.";

	public override string FileToChange => "Services/ImportService.cs";

	public override IReadOnlyList<string> Tasks { get; } =
	[
		"Fix the enrichment stage so every row really is enriched, and so RunImportAsync does not return until it is.",
		"Use the Parallel overload that is built for asynchronous bodies, and forward the CancellationToken that overload hands to your body.",
		"Bound the enrichment stage with its own ParallelOptions, so you do not fire every call at the customer API at once. Pick a limit and be ready to explain the number you picked. The CPU stage and the I/O stage do not have to agree on the same ceiling.",
		"Do not add a shared counter, list, or dictionary that parallel bodies write to. Each call only writes CustomerTier on its own row.",
		"Leave CustomerApiService.cs alone. Its counters are how this step sees how many calls ran at the same time.",
	];

	public override IReadOnlyList<string> Clues { get; } =
	[
		"The enrichment stage calls Parallel.ForEach with an async lambda, and the compiler is perfectly happy with it.",
		"The stage that should be waiting on thousands of calls at 10 milliseconds each reports a hundredth of a second.",
		"RunImportAsync is an async method whose only await is await Task.Yield().",
		"The body Parallel.ForEach takes here is an Action<T>, and no overload of it accepts a Task-returning body. An async lambda bound to a delegate that returns void becomes async void.",
		"Parallel has an asynchronous sibling of ForEach. Its body receives a CancellationToken and returns a ValueTask. Without a MaxDegreeOfParallelism of your own, it runs one body per processor, and cores are the wrong unit for network calls.",
	];

	public override string TimeoutHint => "The import never finished. Check that every customer API call can complete, and that the enrichment stage awaits the calls instead of blocking a thread on each one.";

	public override async Task Run(StepContext context)
	{
		var report = context.Report;

		// Part 1: a fresh CustomerApiService counts how many calls are in flight, so this step can see what the import really waited for
		var customerApi = new CustomerApiService();
		var importService = new ImportService(new OrderFileService(), customerApi);

		report.Log($"Importing {_rowCount:N0} rows through a fresh ImportService and CustomerApiService");

		// CancellationToken.None, never context.Token: until this step is fixed, the enrichment stage runs async void lambdas,
		// and a cancelled token reaching one of them throws where nothing can catch it, which ends the whole app
		var importReport = await importService.RunImportAsync(_rowCount, CancellationToken.None).ConfigureAwait(false);

		// Read the moment RunImportAsync returns. A call still in flight is work the import reported on before it finished.
		var callsInFlight = customerApi.CallsInFlight;
		var callsCompleted = customerApi.CallsCompleted;
		var peakCallsInFlight = customerApi.PeakCallsInFlight;

		report.Log($"RunImportAsync returned after {importReport.EnrichElapsed.TotalSeconds:F2}s of enrichment with {importReport.RowsEnriched:N0} rows enriched. "
			+ $"The customer API had answered {callsCompleted:N0} calls, and {callsInFlight:N0} were still in flight");

		report.Expect("Every row has a customer tier when RunImportAsync returns", _rowCount, importReport.RowsEnriched, _notAwaitedHint);
		report.Expect("No customer API call is still in flight when RunImportAsync returns", 0, callsInFlight, _inFlightHint);

		// Part 2: how many calls ran at the same time. More than one is parallel, and fewer than every row is bounded.
		if (peakCallsInFlight >= _rowCount)
			report.Log($"All {_rowCount:N0} calls were in flight at the same time");
		else if (peakCallsInFlight <= 1)
			report.Log("Only one call was ever in flight, so the calls ran back to back");
		else if (peakCallsInFlight == Environment.ProcessorCount)
			report.Log($"At most {peakCallsInFlight} calls ran at once, the same as this machine's {Environment.ProcessorCount} processors. That is the limit Parallel.ForEachAsync uses when you do not choose one. Be ready to explain why the customer API should get that number");
		else
			report.Log($"At most {peakCallsInFlight} calls ran at once, and this machine has {Environment.ProcessorCount} processors. Network calls spend their time waiting, so their limit comes from what the customer API can take, not from the core count");

		report.Expect("Several customer API calls run at the same time", "more than 1", $"{peakCallsInFlight:N0}", peakCallsInFlight > 1, _oneAtATimeHint);
		report.Expect("The customer API is never sent every row at once", $"fewer than {_rowCount:N0}", $"{peakCallsInFlight:N0}", peakCallsInFlight < _rowCount, _unboundedHint);

		// Part 3: the compiled code. An async void lambda can look like it works on a good night, so this step reads ImportService's IL to find any that are left.
		var callsForEachAsync = CodeInspector.GetCalledMethods(typeof(ImportService)).Contains("Parallel.ForEachAsync");
		var asyncVoidMethods = CodeInspector.FindAsyncVoidMethods(typeof(ImportService));

		report.Log($"ImportService {(callsForEachAsync ? "calls" : "does not call")} Parallel.ForEachAsync, and has {asyncVoidMethods.Count} async void lambdas or methods");

		report.Expect("ImportService calls Parallel.ForEachAsync", "called", callsForEachAsync ? "called" : "not called", callsForEachAsync, _forEachAsyncHint);
		report.Expect("ImportService has no async void lambdas or methods left", 0, asyncVoidMethods.Count, _asyncVoidHint);
	}
}