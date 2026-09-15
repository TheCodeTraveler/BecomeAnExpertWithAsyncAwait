namespace ProductDetails;

public sealed class Step1StartEveryCall : WorkshopStep
{
	// Every card has answered by the time the slowest service has, give or take a busy laptop
	const double _latestCardSeconds = 1.4;

	// An await hands Blazor's renderer back within milliseconds. A blocking wait holds it until every task has finished.
	const double _longestRendererPauseSeconds = 0.3;

	const string _neverFinishedHint = "The page never finished loading, and nothing threw. A blocking wait such as .Wait(), .Result or Task.WaitAll holds the thread Blazor renders on, and SetPanelAsync() needs that same thread to record a card, so they wait for each other forever. "
		+ "Await Task.WhenAll, Task.WhenAny or Task.WhenEach instead. If you loop over Task.WhenAny, remove each finished task from the list, or the loop never ends.";

	const string _oneAfterAnotherHint = "The services still answer one after another. await does not start work: calling the service method does, and each await here holds up the call below it. "
		+ "Call all five service methods first, keep the Task each one returns, and only then await them together.";

	const string _finishedTooEarlyHint = "The page stopped loading before its slowest service could have answered. Starting every call is only half of it: "
		+ "the page still has to await all five, with Task.WhenAll, Task.WhenAny or Task.WhenEach, before it stops loading.";

	const string _runningTotalHint = "These cards waited for the services above them before their own calls even started, so each timing is a running total. "
		+ "Start every call before awaiting any of them, and each card's timing becomes the cost of its own service.";

	const string _blockedRendererHint = "Something held the thread Blazor renders on while the services were running. .Wait(), .Result and Task.WaitAll hold the thread until every task finishes, so the page cannot repaint, and in the browser the tab freezes. "
		+ "Await Task.WhenAll, Task.WhenAny or Task.WhenEach instead.";

	public override int Number => 1;

	public override string Scenario => "Start every call before awaiting any of them";

	public override string Title => "LoadProductAsync()";

	public override string Story => "Wireless Headphones is the launch day deal, and its product page takes over four seconds to load. Analytics shows a third of visitors give up before anything appears. "
		+ "Every backend service it needs answers in 1.2 seconds or less, and none of them needs another one's answer. "
		+ "The page is not slow because its services are slow. It is slow because it asks them one at a time.";

	public override string SeeItInTheApp => "Reload the browser tab, or press Load product page. Nothing happens for about four seconds while every card says waiting, then four cards appear at the same instant and the total page load tile reads 4.2s. "
		+ "The card timings read 0.7s, 1.6s, 2.8s and 3.4s. Those are running totals: each card also paid for every service above it.";

	public override string FileToChange => "Components/Pages/Product.razor.cs";

	public override IReadOnlyList<string> Tasks { get; } =
	[
		"Start all five backend calls before you await any of them.",
		"Coordinate them with Task.WhenAll, Task.WhenAny, or Task.WhenEach, and await it rather than blocking on it.",
		"Keep the total page load at the cost of the slowest service, not the sum of all five.",
		"Keep passing CancellationToken.None to every service call, so the call sites stay ready for a real token.",
		"Keep using ConfigureAwait(false) on every await.",
	];

	public override IReadOnlyList<string> Clues { get; } =
	[
		"Every await in LoadProductAsync() sits on the same line that starts the call, so the next service cannot start until the previous one has answered.",
		"The five services are injected separately and share no state, so nothing about the data forces this order.",
		"The slowest single service takes 1.2 seconds, and 1.2 is a lot smaller than 4.2.",
		"await does not start work. Calling GetInventoryAsync() starts it. Call all five methods first, keep the five tasks, and await them afterwards.",
	];

	public override string TimeoutHint => "The Product page never finished loading. Check for a blocking wait such as .Wait(), .Result or Task.WaitAll, and for a Task.WhenAny loop that never removes the task that finished.";

	public override async Task Run(StepContext context)
	{
		var report = context.Report;
		var token = context.Token;

		// Part 1: open the Product page the way a browser tab does, with fresh copies of the five services
		await using var session = new ProductPageSession();

		report.Log($"Opening the Product page. Its five services cost {ProductPageSession.AllServicesSeconds:F1}s one after another, and {ProductPageSession.SlowestServiceSeconds:F1}s side by side");

		var load = await session.OpenPageAsync(token).ConfigureAwait(false);

		if (!load.Finished)
		{
			report.Log($"Still loading after {ProductPageSession.LoadTimeout.TotalSeconds:F0} seconds. Giving up");
			report.Expect("The Product page finishes loading", $"within {ProductPageSession.LoadTimeout.TotalSeconds:F0} seconds", "still loading", false, _neverFinishedHint);
			return;
		}

		var seconds = load.Elapsed.TotalSeconds;

		report.Log($"The page finished loading in {seconds:F2}s, and its total page load tile reads {(load.TotalSeconds is { } totalSeconds ? $"{totalSeconds:F1}s" : "nothing")}");
		report.Log($"The cards read: {ProductPageSession.DescribeCards(load.Panels)}");

		// Part 2: five independent calls side by side cost the slowest one. Faster than that means the page stopped waiting too soon.
		report.Expect(
			$"The page load costs the slowest service ({ProductPageSession.SlowestServiceSeconds:F1}s), not the sum of all five ({ProductPageSession.AllServicesSeconds:F1}s)",
			$"about {ProductPageSession.SlowestServiceSeconds:F1}s, at most {ProductPageSession.LongestPageLoadSeconds:F1}s",
			$"{seconds:F1}s",
			seconds is >= ProductPageSession.ShortestPageLoadSeconds and <= ProductPageSession.LongestPageLoadSeconds,
			seconds < ProductPageSession.ShortestPageLoadSeconds ? _finishedTooEarlyHint : _oneAfterAnotherHint);

		// Part 3: when every call starts at once, no service can answer later than the slowest one, so no card can report a running total
		var lateCards = load.Panels.Where(static panel => panel.Seconds > _latestCardSeconds).ToList();

		report.Expect(
			"No card reports a running total, because every service answers by the time the slowest one does",
			$"no card later than {_latestCardSeconds:F1}s",
			lateCards.Count is 0 ? $"no card later than {_latestCardSeconds:F1}s" : string.Join(", ", lateCards.Select(static panel => $"{panel.Name} at {panel.Seconds:F1}s")),
			lateCards.Count is 0,
			_runningTotalHint);

		// Part 4: awaiting never holds Blazor's renderer. Blocking on the tasks does, and the page cannot repaint until they finish.
		var longestPause = load.LongestRendererPause.TotalSeconds;

		report.Log($"While the page loaded, Blazor's renderer was held up for {longestPause:F2}s at most");
		report.Expect(
			"Blazor's renderer is never held up while the page waits for its services",
			$"no pause longer than {_longestRendererPauseSeconds:F1}s",
			$"longest pause {longestPause:F2}s",
			longestPause <= _longestRendererPauseSeconds,
			_blockedRendererHint);
	}
}