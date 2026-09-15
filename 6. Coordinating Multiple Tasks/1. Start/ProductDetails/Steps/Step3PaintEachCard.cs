namespace ProductDetails;

public sealed class Step3PaintEachCard : WorkshopStep
{
	// Five services answer at five different moments, so a page that paints each card as it arrives passes through 1, 2, 3 and 4 answered cards on the way to 5.
	// Asking for 3 of those 4 paints leaves room for two services answering within the same instant on a busy laptop.
	const int _minimumPartialPaints = 3;

	// A card's timing can run a little late on a busy laptop, but never much earlier than its service could answer
	const double _earliestCardOffsetSeconds = 0.1;
	const double _latestCardOffsetSeconds = 0.25;

	const string _neverFinishedHint = "The page never finished loading, and nothing threw. If you loop over Task.WhenAny, remove each finished task from the list: a finished task stays finished, so the next call returns it again and the loop never ends. "
		+ "Check too that nothing blocks on a task with .Wait(), .Result or Task.WaitAll.";

	const string _oneRepaintHint = "Every card still reaches the screen in one paint at the end. SetPanelAsync() records a card, but recording is not repainting, and Task.WhenAll only completes once, when the slowest task does. "
		+ "Repaint with InvokeAsync(StateHasChanged) each time a service answers: stream the tasks as they finish with Task.WhenEach or Task.WhenAny, or repaint from inside each call's own wrapper.";

	const string _declarationOrderHint = "The page repaints, but not each time a service answers, so some cards reach the screen together. That happens when the page waits for the calls in the order you wrote them rather than the order they finish. "
		+ "Repaint with InvokeAsync(StateHasChanged) as each task finishes: stream the tasks with Task.WhenEach or Task.WhenAny, or repaint from inside each call's own wrapper.";

	const string _shippingHint = "Shipping answers first, at 0.6s, but it did not reach the screen until Reviews answered at 1.2s. "
		+ "Handle the calls in the order the services finish, not the order you wrote them, and repaint as each one does.";

	const string _timingHint = "A card's timing should be the moment its own service answered, successful or not. A card that waited for another service reports that service's time instead. "
		+ "Read the stopwatch when that one call finishes.";

	const string _againHint = "The second load did not match the first. LoadProductAsync() runs again every time the button is pressed, so it has to start fresh calls, reset every card, and time the new load from its own start.";

	const string _isLoadingHint = "IsLoading has to go back to false after every load, including one where a service failed, or the Load product page button stays disabled. Set it through InvokeAsync, the way the starter does.";

	public override int Number => 3;

	public override string Scenario => "Paint each card as its service answers";

	public override string Title => "LoadProductAsync() and StateHasChanged()";

	public override string Story => "Customers do not wait for a page. They wait for the part they came for. Shipping knows the delivery date in 0.6 seconds, "
		+ "but a page that paints once at the end hides it until the slowest service has answered. "
		+ "The page should fill in card by card, and each card's timing should say when its own service answered, so the numbers on the page tell the truth about the backend.";

	public override string SeeItInTheApp => "On the Product page, press Load product page and watch the cards. They should appear one at a time: Shipping at 0.6s, Inventory at 0.7s, Recommendations failing at 0.8s, Pricing at 0.9s and Reviews at 1.2s. "
		+ "If they all appear together at the end, the page only repaints once. Press the button a few times: the result should be the same every time.";

	public override string FileToChange => "Components/Pages/Product.razor.cs";

	public override IReadOnlyList<string> Tasks { get; } =
	[
		"Repaint the page as each service answers instead of once at the end.",
		"Keep each card's timing the elapsed time at which that service answered.",
		"Keep marshaling each repaint back through InvokeAsync(StateHasChanged), because after ConfigureAwait(false) the code is no longer on Blazor's renderer.",
		"Make Load product page give the same result every time, and leave the button enabled when the load finishes.",
	];

	public override IReadOnlyList<string> Clues { get; } =
	[
		"InvokeAsync(StateHasChanged) only runs before the work starts and after all of it ends, so nothing repaints in between.",
		"SetPanelAsync() records a card on the renderer, but it does not repaint the page.",
		"Task.WhenAll completes once, when the slowest task finishes. Task.WhenEach hands you each task the moment that task finishes.",
		"Task.WhenAny is the manual version of Task.WhenEach. Remove each finished task from the list, or the next call returns it again.",
	];

	public override string TimeoutHint => "The Product page never finished loading. If you loop over Task.WhenAny, remove each finished task from the list, or the loop never ends.";

	public override async Task Run(StepContext context)
	{
		var report = context.Report;
		var token = context.Token;

		// Part 1: open the Product page and record every time it paints
		await using var session = new ProductPageSession();

		report.Log("Opening the Product page and recording every paint");

		var load = await session.OpenPageAsync(token).ConfigureAwait(false);

		if (!load.Finished)
		{
			report.Log($"Still loading after {ProductPageSession.LoadTimeout.TotalSeconds:F0} seconds. Giving up");
			report.Expect("The Product page finishes loading", $"within {ProductPageSession.LoadTimeout.TotalSeconds:F0} seconds", "still loading", false, _neverFinishedHint);
			return;
		}

		// Blazor can paint the same cards more than once in a row, so only a paint that changed a card is logged
		PagePaint? previousPaint = null;

		foreach (var paint in load.Paints)
		{
			if (previousPaint is null || !paint.Panels.SequenceEqual(previousPaint.Panels))
			{
				var answeredPanels = paint.Panels.Where(static panel => panel.Status is not "waiting");

				report.Log(paint.AnsweredCards is 0
					? $"Painted at {paint.Elapsed.TotalSeconds:F2}s: every card is waiting"
					: $"Painted at {paint.Elapsed.TotalSeconds:F2}s: {paint.AnsweredCards} of {paint.Panels.Count} cards answered. {ProductPageSession.DescribeCards(answeredPanels)}");
			}

			previousPaint = paint;
		}

		// Part 2: between the first paint and the last, the page paints each time a service answers
		var partialPaints = load.Paints
			.Select(static paint => paint.AnsweredCards)
			.Where(static answeredCards => answeredCards is > 0 and < 5)
			.Distinct()
			.Order()
			.ToList();

		report.Expect(
			"Cards appear one at a time as their services answer",
			$"paints with 1, 2, 3 and 4 cards answered (at least {_minimumPartialPaints} of them)",
			partialPaints.Count is 0 ? "no paint between the first and the last" : $"paints with {string.Join(", ", partialPaints)} cards answered",
			partialPaints.Count >= _minimumPartialPaints,
			partialPaints.Count is 0 ? _oneRepaintHint : _declarationOrderHint);

		// Shipping costs 0.6s and Reviews costs 1.2s, so a page that paints as services answer shows one without the other for 0.6 seconds
		var shippingBeforeReviews = load.Paints.FirstOrDefault(static paint => paint.StatusOf("Shipping") is "ready" && paint.StatusOf("Reviews") is "waiting");

		report.Expect(
			"A paint shows Shipping (0.6s) ready while Reviews (1.2s) is still waiting",
			"painted",
			shippingBeforeReviews is null ? "never painted" : $"painted at {shippingBeforeReviews.Elapsed.TotalSeconds:F1}s",
			shippingBeforeReviews is not null,
			_shippingHint);

		// Part 3: each card's timing is its own service's cost
		var timings = ProductPageSession.ServiceLatencies
			.Select(service => (service.Name, Expected: service.Seconds, Actual: load.FindPanel(service.Name)?.Seconds))
			.ToList();

		report.Expect(
			"Each card's timing is when its own service answered",
			string.Join(", ", timings.Select(static timing => $"{timing.Name} {timing.Expected:F1}s")),
			string.Join(", ", timings.Select(static timing => timing.Actual is { } actual ? $"{timing.Name} {actual:F1}s" : $"{timing.Name} --")),
			timings.All(static timing => timing.Actual >= timing.Expected - _earliestCardOffsetSeconds && timing.Actual <= timing.Expected + _latestCardOffsetSeconds),
			_timingHint);

		// Part 4: press Load product page, and get the same page again
		report.Log("Pressing Load product page");

		var again = await session.PressLoadProductPageAsync(token).ConfigureAwait(false);

		if (!again.Finished)
		{
			report.Log($"Still loading after {ProductPageSession.LoadTimeout.TotalSeconds:F0} seconds. Giving up");
			report.Expect("Pressing Load product page finishes loading", $"within {ProductPageSession.LoadTimeout.TotalSeconds:F0} seconds", "still loading", false, _neverFinishedHint);
			return;
		}

		var againSeconds = again.Elapsed.TotalSeconds;

		report.Log($"The page finished loading again in {againSeconds:F2}s, after {again.Paints.Count} paints");
		report.Log($"The cards read: {ProductPageSession.DescribeCards(again.Panels)}");

		var sameCards = load.Panels.Select(static panel => (panel.Name, panel.Status, panel.Detail))
			.SequenceEqual(again.Panels.Select(static panel => (panel.Name, panel.Status, panel.Detail)));

		report.Expect(
			"Pressing Load product page again gives the same cards in the same time",
			$"{DescribeStatuses(load.Panels)}, in about {ProductPageSession.SlowestServiceSeconds:F1}s",
			$"{DescribeStatuses(again.Panels)}, in {againSeconds:F1}s",
			sameCards && againSeconds is >= ProductPageSession.ShortestPageLoadSeconds and <= ProductPageSession.LongestPageLoadSeconds,
			_againHint);

		report.Expect("IsLoading is false again when the load finishes, so the button is enabled", false, again.IsLoading, _isLoadingHint);
	}

	static string DescribeStatuses(IEnumerable<PanelState> panels) => string.Join(", ", panels.Select(static panel => $"{panel.Name} {panel.Status}"));
}