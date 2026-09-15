namespace StockWatch;

public sealed class Step2KeepEveryQuote : WorkshopStep
{
	const string _newerSymbol = "AAPL";
	const string _olderSymbol = "MSFT";

	const string _collectionHint = "Dictionary<TKey, TValue> supports one writer at a time, and RefreshQuotes() writes all 60 quotes from Parallel.ForEachAsync workers at once. "
		+ "A lost insert is a card stuck on --, and a corrupted dictionary can throw or lose every quote. Replace it with a keyed collection from System.Collections.Concurrent that is built for many concurrent writers.";

	const string _newerQuoteHint = "The refresh replaced a newer quote with an older one. TryAdd followed by an indexer assignment, or an update that always stores the incoming quote, lets a slow refresh overwrite a quote that already landed. "
		+ "Add or update each quote in a single call that keeps whichever quote has the newer Timestamp.";

	const string _olderQuoteHint = "The refresh has to update a quote that is already there, not only add the missing ones. Compare the two Timestamps, and store the newer quote.";

	const string _everyQuoteHint = "Every symbol needs a quote after a refresh, or its card shows -- instead of a price. Check that every quote RefreshQuotes() fetches is added or updated.";

	public override int Number => 2;

	public override string Scenario => "Keep every quote, and keep the newest";

	public override string Title => "_latestQuotes and RefreshQuotes()";

	public override string Story => "Every 2 seconds, RefreshQuotes() fetches all 60 quotes at once through Parallel.ForEachAsync and writes each one into _latestQuotes the moment it arrives. "
		+ "Dictionary<TKey, TValue> supports one writer at a time, so a concurrent insert can lose a quote, corrupt the dictionary, or throw. "
		+ "And when a slow tick overlaps the next one, TryAdd followed by an indexer assignment lets an older quote overwrite a newer one that already landed. A trader sees a stale price, with nothing to say it is stale.";

	public override string SeeItInTheApp => "On the Dashboard page, look for cards that show -- instead of a price. GetSymbols() builds a card for every symbol either way, so a card showing -- is a quote that never made it into _latestQuotes. "
		+ "It is rarer than a missing card, so reload a few times. A stale price looks exactly like a real one, which is why this step checks for it directly.";

	public override string FileToChange => "Components/Pages/Dashboard.razor.cs";

	public override IReadOnlyList<string> Tasks { get; } =
	[
		"Replace _latestQuotes with a collection from System.Collections.Concurrent that is built for many concurrent writers.",
		"Replace the TryAdd and the indexer assignment with a single call that adds the quote, or updates the existing one, keeping whichever quote has the newer Timestamp.",
		"Keep the delegate you pass to that call cheap and free of side effects. It runs outside the collection's internal lock, and it can run more than once.",
		"Keep the InvokeAsync(StateHasChanged) call and the CancellationToken plumbing that are already in RefreshQuotes().",
	];

	public override IReadOnlyList<string> Clues { get; } =
	[
		"A thread-safe TryAdd followed by a thread-safe indexer assignment is still two operations. Another thread can write between them.",
		"Quotes arrive in whatever order the feed answers. When two refreshes overlap, the last write is not always the newest quote.",
		"Keyed lookup from many concurrent writers is exactly what ConcurrentDictionary<TKey, TValue> is for.",
		"AddOrUpdate takes the key, the value to add when the key is missing, and a delegate that picks the value to store when it is already there. Compare the two Timestamps, return the winner, and do nothing else.",
	];

	public override string TimeoutHint => "RefreshQuotes() never returned. Check that nothing in it waits on a lock or a semaphore, and that it still passes its CancellationToken to Parallel.ForEachAsync.";

	public override async Task Run(StepContext context)
	{
		var report = context.Report;
		var token = context.Token;

		if (!DashboardHarness.HasMembers(report, "_latestQuotes", "RefreshQuotes", "StopRefreshTimer"))
			return;

		// Part 1: the collection itself. A lost insert is rare enough that one refresh can get lucky, so the type is checked directly.
		var quotesType = DashboardHarness.FindFieldType("_latestQuotes");
		report.Log($"_latestQuotes is a {FormatType(quotesType)} from {quotesType?.Namespace}");

		report.Expect(
			"_latestQuotes is a collection from System.Collections.Concurrent",
			"System.Collections.Concurrent",
			$"{FormatType(quotesType)} from {quotesType?.Namespace}",
			quotesType?.Namespace is "System.Collections.Concurrent",
			_collectionHint);

		// Part 2: a fresh dashboard, the same page a new browser tab gets
		await using var harness = await DashboardHarness.Render().ConfigureAwait(false);

		if (!await harness.LoadAndStopTimer(report, token).ConfigureAwait(false))
			return;

		report.Expect("After loading, every symbol has a quote", $"{DashboardHarness.SymbolCount} of {DashboardHarness.SymbolCount}", $"{CountQuotes(harness)} of {DashboardHarness.SymbolCount}", _everyQuoteHint);

		if (harness.GetField("_latestQuotes") is not IDictionary<string, StockQuoteModel> latestQuotes)
		{
			report.Expect("_latestQuotes can be read by symbol", "a dictionary of StockQuoteModel by symbol", FormatType(quotesType), false, "The dashboard looks up each card's quote by its symbol, so _latestQuotes has to stay a keyed collection of StockQuoteModel by symbol string.");
			return;
		}

		// Part 3: two refreshes overlap. A quote timestamped an hour from now has already landed for one symbol, and a quote from an hour ago for another.
		var newerQuote = await harness.Dashboard.MarketDataService.GetStockQuote(_newerSymbol, token).ConfigureAwait(false) with { Timestamp = DateTimeOffset.UtcNow.AddHours(1) };
		var olderQuote = await harness.Dashboard.MarketDataService.GetStockQuote(_olderSymbol, token).ConfigureAwait(false) with { Timestamp = DateTimeOffset.UtcNow.AddHours(-1) };

		latestQuotes[_newerSymbol] = newerQuote;
		latestQuotes[_olderSymbol] = olderQuote;

		report.Log($"Stored a quote for {_newerSymbol} timestamped an hour from now and a quote for {_olderSymbol} from an hour ago, then called RefreshQuotes(), the way an overlapping timer tick would");

		var refreshOutcome = await DashboardHarness.Describe(harness.RefreshQuotes(token), TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
		report.Log($"RefreshQuotes() {refreshOutcome}");

		if (refreshOutcome is not DashboardHarness.Finished)
		{
			report.Expect("RefreshQuotes() finishes", DashboardHarness.Finished, refreshOutcome, false, "RefreshQuotes() has to finish every refresh, and only an OperationCanceledException from its own token is expected. Check what the code that stores each quote can throw or wait on.");
			return;
		}

		var keptNewerQuote = latestQuotes.TryGetValue(_newerSymbol, out var storedNewerQuote) && storedNewerQuote == newerQuote;
		var replacedOlderQuote = latestQuotes.TryGetValue(_olderSymbol, out var storedOlderQuote) && storedOlderQuote.Timestamp > olderQuote.Timestamp;

		report.Expect($"A newer quote for {_newerSymbol}, timestamped an hour from now, survives the refresh", "kept", keptNewerQuote ? "kept" : "replaced by an older quote", keptNewerQuote, _newerQuoteHint);
		report.Expect($"An older quote for {_olderSymbol}, from an hour ago, is replaced by the refresh", "replaced", replacedOlderQuote ? "replaced" : "still the old quote", replacedOlderQuote, _olderQuoteHint);
		report.Expect("After the refresh, every symbol still has a quote", $"{DashboardHarness.SymbolCount} of {DashboardHarness.SymbolCount}", $"{CountQuotes(harness)} of {DashboardHarness.SymbolCount}", _everyQuoteHint);
	}

	// Counts the cards showing a price, the same way the grid decides between a price and --
	static int CountQuotes(DashboardHarness harness) => harness.Dashboard.Symbols.Count(static card => card.Quote is not null);

	static string FormatType(Type? type) => type switch
	{
		null => "missing field",
		{ IsGenericType: true } => $"{type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)]}<{string.Join(", ", type.GetGenericArguments().Select(FormatType))}>",
		_ when type == typeof(string) => "string",
		_ => type.Name,
	};
}