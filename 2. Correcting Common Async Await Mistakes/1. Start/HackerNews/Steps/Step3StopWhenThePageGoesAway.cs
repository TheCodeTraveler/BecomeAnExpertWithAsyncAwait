using System.Diagnostics;

namespace HackerNews;

public sealed class Step3StopWhenThePageGoesAway : WorkshopStep
{
	const int _storiesBeforeClosing = 10;

	// Stopping a refresh takes a few milliseconds. A refresh that ignores its token runs out the rest of its 2 second minimum.
	static readonly TimeSpan _maximumStopTime = TimeSpan.FromMilliseconds(500);

	static readonly TimeSpan _responseTime = TimeSpan.FromMilliseconds(10);

	public override int Number => 3;

	public override string Scenario => "Stop refresh work when the page goes away";

	public override string Title => "RefreshAsync(CancellationToken) and Dispose()";

	public override string Story => "People open the Top stories page, glance at it, and leave. When they do, Blazor disposes the page, and Dispose() cancels the page's CancellationTokenSource so that its refresh can stop. "
		+ "RefreshAsync(CancellationToken) receives that token and passes it to Hacker News, but not to its 2 second minimum refresh time, so every abandoned page keeps its refresh alive on the server until the delay runs out. "
		+ "The other half of ending well is failing well: when Hacker News is down, the page has to say so in words a user can act on, while the details go to the server log.";

	public override string SeeItInTheApp => "On the Top stories page, press Refresh, and reload the browser tab straight away. The page is gone from your browser, but its refresh keeps running on the server until the 2 second minimum ends. "
		+ "If you are offline, or your network blocks Hacker News, the page shows its refresh error instead of stories: a short message you can act on, with the details in the app's log output (your IDE's Run or Debug output window, or the terminal).";

	public override string FileToChange => "Components/Pages/News.razor.cs";

	public override IReadOnlyList<string> Tasks { get; } =
	[
		"Forward the CancellationToken that RefreshAsync(CancellationToken) receives to every cancellable async API it calls, including the minimum refresh time.",
		"When the page goes away mid-refresh, let the refresh end quietly: no exception may escape it. Catch OperationCanceledException only when your own token was canceled.",
		"Keep logging the full exception on the server, and keep showing users a generic, actionable refresh error. Never put exception text on the page.",
	];

	public override IReadOnlyList<string> Clues { get; } =
	[
		"A cancellation token is accepted but not always forwarded.",
		"Dispose() cancels _disposeCancellationTokenSource. Follow its token through RefreshAsync(CancellationToken), and look for an async call that never sees it.",
		"Once the minimum refresh time listens to the token, awaiting it after the page goes away throws TaskCanceledException, and that await is in a finally block.",
		"ConfigureAwaitOptions.SuppressThrowing awaits a Task without throwing when it is canceled or faulted. It works on Task, not on Task<T>.",
	];

	public override string TimeoutHint => "A refresh never finished after the page closed, or after Hacker News failed. Check that every await in RefreshAsync(CancellationToken) can end once its token is canceled.";

	public override async Task Run(StepContext context)
	{
		var report = context.Report;
		var token = context.Token;

		if (NewsPageSession.HasAsyncVoidMethods(report))
			return;

		await ClosePageMidRefresh(report, token).ConfigureAwait(false);

		await FailRefresh(report, token).ConfigureAwait(false);
	}

	// Part 1: press Refresh, then close the page while the refresh is still loading stories, the way a user navigates away
	static async Task ClosePageMidRefresh(StepReport report, CancellationToken token)
	{
		var hackerNewsApi = new FakeHackerNewsAPI(_responseTime);
		await using var session = new NewsPageSession(hackerNewsApi);

		report.Log("Rendering the News page and waiting for it to finish initializing");
		await session.BeginRendering(token).ConfigureAwait(false);

		if (!await session.WaitForInitialization(token).ConfigureAwait(false))
		{
			report.Expect("The page finishes initializing", "within 10 seconds", "did not finish", false, "Step 1 checks this. Make it pass again first.");
			return;
		}

		var requestsBeforeRefresh = hackerNewsApi.StoryRequests;

		report.Log("Pressing Refresh");
		var refreshTask = session.PressRefresh();

		var waitForStories = Stopwatch.StartNew();

		while (hackerNewsApi.StoryRequests - requestsBeforeRefresh < _storiesBeforeClosing && !refreshTask.IsCompleted && waitForStories.Elapsed < NewsPageSession.RenderTimeout)
		{
			await Task.Delay(TimeSpan.FromMilliseconds(5), token).ConfigureAwait(false);
		}

		var requestsWhenClosed = hackerNewsApi.StoryRequests;
		report.Log($"Closing the page after the refresh asked Hacker News for {requestsWhenClosed - requestsBeforeRefresh} stories");

		var stopwatch = Stopwatch.StartNew();
		await session.ClosePage().ConfigureAwait(false);

		Exception? escapedException = null;

		try
		{
			await refreshTask.WaitAsync(NewsPageSession.RenderTimeout, token).ConfigureAwait(false);
		}
		catch (Exception e) when (e is not TimeoutException && !token.IsCancellationRequested)
		{
			escapedException = e;
		}

		var stopTime = stopwatch.Elapsed;
		var requestsAfterClosing = hackerNewsApi.StoryRequests - requestsWhenClosed;

		report.Log($"The refresh ended {stopTime.TotalMilliseconds:0} ms after the page closed, and asked Hacker News for {requestsAfterClosing} more stories after that");

		if (escapedException is not null)
			report.Log($"The refresh ended with {escapedException.GetType().Name}");

		report.Expect(
			"After the page closes mid-refresh, the refresh ends quickly",
			$"within {_maximumStopTime.TotalMilliseconds:0} ms",
			$"{stopTime.TotalMilliseconds:0} ms",
			stopTime <= _maximumStopTime,
			"The page was gone, but its refresh kept running until the minimum refresh time ran out. Dispose() cancels the token RefreshAsync(CancellationToken) receives, and a cancellable async call can only stop early if it is given that token.");

		// One request can start at the very moment the page closes
		report.Expect(
			"The refresh asks Hacker News for no more stories after the page closes",
			"at most 1",
			$"{requestsAfterClosing}",
			requestsAfterClosing <= 1,
			"The refresh kept fetching stories for a page nobody can see. Forward the CancellationToken to every call that accepts one, all the way down to HackerNewsApiService.");

		report.Expect(
			"No exception escapes the refresh when the page closes",
			"none",
			escapedException?.GetType().Name ?? "none",
			escapedException is null,
			"In the app, an exception that escapes a Refresh click shows Blazor's unhandled error bar and ends the user's connection. Awaiting a canceled task throws, even in a finally block. "
				+ "Await the minimum refresh time in a way that does not throw when it is canceled, and catch OperationCanceledException only when your own token was canceled.");
	}

	// Part 2: load the page while Hacker News is down
	static async Task FailRefresh(StepReport report, CancellationToken token)
	{
		await using var session = new NewsPageSession(new FakeHackerNewsAPI(_responseTime) { IsFailing = true });

		report.Log("Rendering the News page while every Hacker News call throws HttpRequestException");
		await session.BeginRendering(token).ConfigureAwait(false);

		if (!await session.WaitForInitialization(token).ConfigureAwait(false))
		{
			report.Expect("The page finishes initializing when Hacker News is down", "within 10 seconds", "did not finish", false, "Something in the refresh waits forever once a call to Hacker News fails. Check the catch and finally blocks in RefreshAsync(CancellationToken).");
			return;
		}

		var errorMessage = await session.Read(static page => page.ErrorMessage, token).ConfigureAwait(false);
		var isRefreshing = await session.Read(static page => page.IsRefreshing, token).ConfigureAwait(false);
		var html = await session.GetHtml(token).ConfigureAwait(false);
		var errors = session.Logger.Errors;

		report.Log(errorMessage is null ? "The page shows no error message" : $"The page shows the error message \"{errorMessage}\"");
		report.Log(errors.Count is 0 ? "Your page logged no errors" : $"Your page logged: {string.Join(", ", errors.Select(static error => $"\"{error.Message}\" ({error.Exception?.GetType().Name ?? "no exception"})"))}");

		report.Expect(
			"When Hacker News is down, the page shows an error message",
			"an error message",
			errorMessage is null ? "none" : "an error message",
			errorMessage is not null,
			"The refresh failed and the page said nothing. Catch the failure in RefreshAsync(CancellationToken) and set RefreshErrorMessage to a message the user can act on.");

		var showsDetails = ShowsExceptionDetails(errorMessage) || ShowsExceptionDetails(html);

		report.Expect(
			"The page shows no exception details",
			"no exception details",
			showsDetails ? "the exception's message or type" : "no exception details",
			!showsDetails,
			"Exception text can reveal servers, URLs and internals, and it means nothing to the person reading it. Show a generic message that says what to try next, and keep the details in the server log.");

		var loggedFailure = errors.Any(static error => IsFailure(error.Exception));

		report.Expect(
			"The full exception is logged on the server",
			"HttpRequestException logged",
			loggedFailure ? "HttpRequestException logged" : errors.Count is 0 ? "no errors logged" : "logged without the exception",
			loggedFailure,
			"Pass the exception itself to Logger.LogError(...), not just its message, so the server log keeps its type and stack trace.");

		report.Expect(
			"The refresh indicator turns off after a failed refresh",
			"off",
			isRefreshing ? "still on" : "off",
			!isRefreshing,
			"A failed refresh has to stop the indicator too. Turn IsListRefreshing off where it runs whether or not the refresh succeeded.");
	}

	static bool ShowsExceptionDetails(string? text) => text is not null
		&& (text.Contains(FakeHackerNewsAPI.FailureDetail, StringComparison.Ordinal)
			|| text.Contains(nameof(HttpRequestException), StringComparison.Ordinal)
			|| text.Contains("Service Unavailable", StringComparison.Ordinal));

	static bool IsFailure(Exception? exception) => exception switch
	{
		HttpRequestException { Message: FakeHackerNewsAPI.FailureDetail } => true,
		AggregateException aggregateException => aggregateException.InnerExceptions.Any(IsFailure),
		null => false,
		_ => IsFailure(exception.InnerException),
	};
}