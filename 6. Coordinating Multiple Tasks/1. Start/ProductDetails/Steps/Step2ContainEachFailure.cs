namespace ProductDetails;

public sealed class Step2ContainEachFailure : WorkshopStep
{
	const string _failingCard = "Recommendations";

	const string _neverFinishedHint = "The page never finished loading, and nothing threw. Check that every call's error handling completes, and that nothing blocks on a task with .Wait(), .Result or Task.WaitAll.";

	const string _failedStatusHint = "The Recommendations service always throws HttpRequestException, and that failure belongs to its card alone. Catch it around that one call and record the card with the status failed, which Product.razor draws in the failure style. "
		+ "A card that reads skipped was jumped over by a catch it shares with other calls.";

	const string _messageHint = "Put a fixed message on the failed card that tells the customer what happened and what to do next. "
		+ "The exception's own message describes the backend, and it belongs in the log, not in the browser.";

	const string _otherCardsHint = "One failure is still stopping cards that have nothing to do with it. Give every service call its own error handling, so a failure can only reach the card it belongs to. "
		+ "How much of the page survives should not depend on where a call sits in the method.";

	const string _bannerHint = "Every failure now belongs to a card, so the page as a whole has nothing left to report. Stop setting PageError when one service fails, so the yellow banner never appears.";

	const string _exceptionTextHint = "The page shows part of the exception. Anything the page renders reaches the browser, so show a message the page owns and keep the exception in the log.";

	const string _loggingHint = "The terminal is where a failure can be acted on, so it needs the whole exception. Pass the HttpRequestException itself to Logger.LogError, the way the starter does, not just its message.";

	// Words from the exception RecommendationsService throws. None of them belongs in the browser.
	static readonly string[] _exceptionText = ["503", "Service Unavailable", nameof(HttpRequestException)];

	public override int Number => 2;

	public override string Scenario => "Let one failing service break only its own card";

	public override string Title => "LoadProductAsync() and PageError";

	public override string Story => "The recommendations engine belongs to another team, and it is down again: every call returns 503 Service Unavailable. "
		+ "Nobody expects the product page to show recommendations while it is down. They do expect to see the price, the stock and the delivery date. "
		+ "One shared try block turns one broken dependency into a broken page, and how much of the page dies is decided by where the call sits in the method, not by what failed.";

	public override string SeeItInTheApp => "On the Product page, the Recommendations card reads -- and never requested, and a yellow banner says the page load stopped. The banner does not say which service failed. "
		+ "The terminal running the app does: the logged HttpRequestException reads Recommendations service returned 503 Service Unavailable. "
		+ "If you already start every call at once inside that same try block, the other four cards may read never requested too.";

	public override string FileToChange => "Components/Pages/Product.razor.cs";

	public override IReadOnlyList<string> Tasks { get; } =
	[
		"Give each call its own error handling, so a failure is recorded against one card.",
		"Catch HttpRequestException, the exception a failing HTTP dependency throws, rather than every exception.",
		"Set the failing card's status to failed and put a message the page owns on that card.",
		"Keep logging the full exception server-side the way the starter already does, and keep exception text out of the browser.",
		"Stop reporting a single card's failure as a failure of the whole page.",
	];

	public override IReadOnlyList<string> Clues { get; } =
	[
		"One catch (HttpRequestException) covers all five calls, so the first failure skips everything after it.",
		"Move the recommendations call above the reviews call and two more cards go blank. How much of the page dies is decided by the shape of the code, not by the failure.",
		"Awaiting Task.WhenAll rethrows one of the failures, but only after every task has finished, and only once for the whole group.",
		"A small async method that takes one already started Task<T> can await it inside its own try block and record the result, or the failure, on that one card.",
	];

	public override string TimeoutHint => "The Product page never finished loading. Check that every call's error handling completes, and that nothing blocks on a task.";

	public override async Task Run(StepContext context)
	{
		var report = context.Report;
		var token = context.Token;

		// Part 1: open the Product page. The Recommendations service is down and always throws HttpRequestException.
		await using var session = new ProductPageSession();

		report.Log("Opening the Product page. The Recommendations service is down, and every call to it throws HttpRequestException");

		var load = await session.OpenPageAsync(token).ConfigureAwait(false);

		if (!load.Finished)
		{
			report.Log($"Still loading after {ProductPageSession.LoadTimeout.TotalSeconds:F0} seconds. Giving up");
			report.Expect("The Product page finishes loading", $"within {ProductPageSession.LoadTimeout.TotalSeconds:F0} seconds", "still loading", false, _neverFinishedHint);
			return;
		}

		report.Log($"The page finished loading in {load.Elapsed.TotalSeconds:F2}s");
		report.Log($"The cards read: {ProductPageSession.DescribeCards(load.Panels)}");
		report.Log(load.PageError is null ? "There is no warning banner" : $"The warning banner reads: {load.PageError}");

		// Part 2: the service that is down damages its own card
		var failingPanel = load.FindPanel(_failingCard);

		report.Expect($"The {_failingCard} card reads failed", "failed", failingPanel?.Status ?? "missing", _failedStatusHint);

		var message = failingPanel?.Detail;

		report.Expect(
			"The failed card shows a message the page owns",
			"a message with no exception text",
			string.IsNullOrWhiteSpace(message) ? "no message" : message,
			!string.IsNullOrWhiteSpace(message) && !ContainsExceptionText(message),
			_messageHint);

		// Part 3: and only its own card
		var otherPanels = load.Panels.Where(static panel => panel.Name is not _failingCard).ToList();

		report.Expect(
			$"Inventory, Pricing, Reviews and Shipping still load while {_failingCard} is down",
			string.Join(", ", otherPanels.Select(static panel => $"{panel.Name} ready")),
			string.Join(", ", otherPanels.Select(static panel => $"{panel.Name} {panel.Status}")),
			otherPanels.Count is 4 && otherPanels.All(static panel => panel.Status is "ready"),
			_otherCardsHint);

		report.Expect(
			"The page itself does not fail, so the yellow warning banner is gone",
			"no banner",
			load.PageError is null ? "no banner" : $"a banner that reads \"{load.PageError}\"",
			load.PageError is null,
			_bannerHint);

		// Part 4: the whole exception goes to the log, and none of it reaches the browser
		var leakedText = _exceptionText.Where(text => load.Html.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();

		report.Expect(
			"No exception text reaches the browser",
			"none on the page",
			leakedText.Count is 0 ? "none on the page" : $"the page shows {string.Join(", ", leakedText)}",
			leakedText.Count is 0,
			_exceptionTextHint);

		var loggedFailure = session.Logger.Messages.FirstOrDefault(static logged => logged.Level >= LogLevel.Error && (logged.Exception is HttpRequestException || logged.Exception?.InnerException is HttpRequestException));

		report.Log(loggedFailure is null ? "The page logged no HttpRequestException" : $"The page logged at {loggedFailure.Level}: {loggedFailure.Message}");
		report.Expect(
			"The full HttpRequestException is logged server-side",
			"logged at Error, with the exception",
			loggedFailure is null ? "not logged" : $"logged at {loggedFailure.Level}, with the exception",
			loggedFailure is not null,
			_loggingHint);
	}

	static bool ContainsExceptionText(string text) => _exceptionText.Any(exceptionText => text.Contains(exceptionText, StringComparison.OrdinalIgnoreCase));
}