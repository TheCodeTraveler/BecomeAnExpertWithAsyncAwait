namespace StockWatch;

public sealed class Step1ShowEveryCard : WorkshopStep
{
	// One call can get lucky. Two hundred calls in a row, each adding 60 cards from several workers, almost never do.
	const int _callCount = 200;

	const string _missingCardsHint = "Cards went missing, and nothing warned you. List<T>.Add writes into an internal array and sometimes grows it, so two Parallel.ForEach workers adding at once can write the same slot, or write into the old array while another worker replaces it. "
		+ "Collect the cards in a collection that many threads can add to at once.";

	const string _throwsHint = "A torn List<T> leaves an empty slot in its backing array, or throws while it grows. OrderBy then dereferences the empty slot, which is the NullReferenceException behind the Feed fault panel in the browser. "
		+ "Collect the cards in a collection that many threads can add to at once.";

	const string _orderHint = "Parallel.ForEach adds the cards in whatever order its workers finish. Keep the OrderBy on the last line of GetSymbols(), so the grid stays alphabetical.";

	public override int Number => 1;

	public override string Scenario => "Show every card";

	public override string Title => "Symbols and GetSymbols()";

	public override string Story => "Traders keep StockWatch open all day, and they trust the grid: a symbol that is not on it is a symbol they are not watching. "
		+ "The Symbols property builds a card for every one of the 60 symbols from inside Parallel.ForEach, on every render, and List<T> was never built for more than one writer. "
		+ "Cards silently go missing, a different number on every reload, and every so often the render throws and the quote board stops updating altogether.";

	public override string SeeItInTheApp => "On the Dashboard page, count the cards. There should be 60, and you will usually count somewhere in the forties or fifties. "
		+ "Reload a few times and the number changes. Every so often a Feed fault panel replaces the whole dashboard until you reload, and the terminal running the app shows a NullReferenceException from OrderBy.";

	public override string FileToChange => "Components/Pages/Dashboard.razor.cs";

	public override IReadOnlyList<string> Tasks { get; } =
	[
		"Add using System.Collections.Concurrent; to the top of Dashboard.razor.cs.",
		"Replace the List<StockSymbolModel> in GetSymbols() with a thread-safe collection that many Parallel.ForEach workers can add to at once.",
		"Keep the OrderBy on the last line of GetSymbols(), so the cards stay in alphabetical order.",
		"Keep the Parallel.ForEach, and leave MarketDataService, the models, and Dashboard.razor alone. The parallel loop is not the bug.",
	];

	public override IReadOnlyList<string> Clues { get; } =
	[
		"List<T> is not thread safe. Add writes into an internal array and sometimes replaces that array with a bigger one.",
		"A missing card and a NullReferenceException from OrderBy are the same bug: a slot in the list's array that no worker ever filled.",
		"Nothing here needs the cards in the order they were added, because OrderBy sorts them anyway.",
		"System.Collections.Concurrent has a collection built for exactly this job: collect results from many threads now, and sort them later.",
	];

	public override string TimeoutHint => "Symbols never returned. Check that nothing in GetSymbols() waits on a lock or a semaphore that is never released.";

	public override async Task Run(StepContext context)
	{
		var report = context.Report;
		var token = context.Token;

		if (!DashboardHarness.HasMembers(report, "StopRefreshTimer"))
			return;

		// Part 1: a fresh dashboard, the same page a new browser tab gets, with a price for every symbol
		await using var harness = await DashboardHarness.Render().ConfigureAwait(false);

		if (!await harness.LoadAndStopTimer(report, token).ConfigureAwait(false))
			return;

		// Part 2: every render reads Symbols, so read it the way two hundred renders in a row would
		var expectedSymbols = harness.Dashboard.MarketDataService.Symbols.Order().ToList();
		report.Log($"Reading Symbols {_callCount} times. Each read runs Parallel.ForEach over {expectedSymbols.Count} symbols, with up to one worker per core ({Environment.ProcessorCount} on this machine)");

		var completeCalls = 0;
		var orderedCalls = 0;
		var returnedCalls = 0;
		var fewestCards = int.MaxValue;
		var exceptionTypes = new SortedSet<string>(StringComparer.Ordinal);

		for (var call = 0; call < _callCount; call++)
		{
			token.ThrowIfCancellationRequested();

			IReadOnlyList<StockSymbolModel> cards;

			try
			{
				cards = harness.Dashboard.Symbols;
			}
			catch (Exception e)
			{
				// Only the exception type goes to the page
				exceptionTypes.Add(e.GetType().Name);
				continue;
			}

			returnedCalls++;
			fewestCards = Math.Min(fewestCards, cards.Count);

			var symbols = cards.Select(static card => card.Symbol).ToList();

			if (symbols.Order().SequenceEqual(expectedSymbols))
				completeCalls++;

			if (symbols.SequenceEqual(symbols.Order()))
				orderedCalls++;
		}

		var threwCalls = _callCount - returnedCalls;

		report.Log(returnedCalls is 0
			? $"Every read of Symbols threw: {string.Join(", ", exceptionTypes)}"
			: $"{completeCalls} of {_callCount} reads returned all {expectedSymbols.Count} cards. The fewest cards any read returned was {fewestCards}");

		if (threwCalls > 0)
			report.Log($"{threwCalls} reads threw: {string.Join(", ", exceptionTypes)}");

		report.Expect($"{_callCount} reads of Symbols in a row each return a card for all {DashboardHarness.SymbolCount} symbols", $"{_callCount} of {_callCount}", $"{completeCalls} of {_callCount}", completeCalls is _callCount, _missingCardsHint);

		report.Expect($"None of the {_callCount} reads throws", 0, threwCalls, _throwsHint);

		report.Expect(
			"Every read lists its cards in alphabetical order",
			"every read",
			returnedCalls is 0 ? "no read returned" : $"{orderedCalls} of {returnedCalls} reads that returned",
			returnedCalls > 0 && orderedCalls == returnedCalls,
			returnedCalls is 0 ? _throwsHint : _orderHint);
	}
}