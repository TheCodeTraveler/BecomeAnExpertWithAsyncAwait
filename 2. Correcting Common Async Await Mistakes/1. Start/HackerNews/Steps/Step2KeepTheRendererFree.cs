using System.Diagnostics;
using HackerNews.Components.Pages;

namespace HackerNews;

public sealed class Step2KeepTheRendererFree : WorkshopStep
{
	const string _blockingHint = "A blocking wait holds whichever thread runs it until the task finishes. Inside an async method, await the task instead, and keep the 2 second minimum: "
		+ "moving the wait off Blazor's renderer with ConfigureAwait(false), or swapping Wait() for Result or GetAwaiter().GetResult(), still blocks a thread pool thread for every refresh.";

	// A render of 50 stories takes a few milliseconds. A blocking wait on the 2 second minimum holds the renderer for well over a second.
	static readonly TimeSpan _maximumRendererDelay = TimeSpan.FromMilliseconds(500);

	static readonly TimeSpan _heartbeatInterval = TimeSpan.FromMilliseconds(25);

	static readonly TimeSpan _minimumRefreshTime = TimeSpan.FromSeconds(1.9);

	// Every call answers almost at once, so nearly all of a refresh is spent on its 2 second minimum
	static readonly TimeSpan _responseTime = TimeSpan.FromMilliseconds(1);

	public override int Number => 2;

	public override string Scenario => "Keep Blazor's renderer free";

	public override string Title => "RefreshAsync(CancellationToken)";

	public override string Story => "Every browser tab connected to a Blazor Server app has its own renderer, and your component code runs on it one piece of work at a time, much like a UI thread. "
		+ "RefreshAsync() ends by calling Wait() on its 2 second minimum refresh time. When that line runs on the renderer, the tab's clicks, renders and UI updates queue up behind it, and a thread pool thread sits blocked doing nothing. "
		+ "One user pressing Refresh never notices. Two hundred users pressing it at once starve the thread pool for everyone on the server.";

	public override string SeeItInTheApp => "On the Top stories page, press Refresh. The spinner keeps turning, because the browser animates it, so the page looks busy rather than stuck. "
		+ "For most of those 2 seconds, though, the server cannot run anything else for your tab. This step posts a tiny piece of work to the renderer every 25 milliseconds during a refresh, and times how long each one waits.";

	public override string FileToChange => "Components/Pages/News.razor.cs";

	public override IReadOnlyList<string> Tasks { get; } =
	[
		"Replace the blocking wait on the minimum refresh time with await. Keep the 2 second minimum: the refresh indicator should still stop only after it ends.",
		"Do not trade Wait() for Result or GetAwaiter().GetResult(). They block exactly the same way.",
		"Use ConfigureAwait(false) only where the code after the await does not need Blazor's context.",
		"After ConfigureAwait(false), make every change to the page's state, and every StateHasChanged(), through InvokeAsync(...).",
	];

	public override IReadOnlyList<string> Clues { get; } =
	[
		"A blocking wait is used inside an async method.",
		"A continuation is allowed to capture context even when it does not need to. Ask which thread runs the finally block after the await above it.",
		"ConfigureAwait(false) moves the Wait() off the renderer, but it still blocks a thread pool thread. That is why this step reads your code as well as timing the renderer.",
		"UI state changes must be marshaled through Blazor's renderer when continuations run away from the captured context. Blazor throws InvalidOperationException when StateHasChanged() runs anywhere else.",
	];

	public override string TimeoutHint => "A refresh never finished. Check for a blocking wait that runs on Blazor's renderer: the code after each await needs the renderer to finish, and a wait that holds it can hold it forever.";

	public override async Task Run(StepContext context)
	{
		var report = context.Report;
		var token = context.Token;

		// Part 1: read the compiled page for blocking waits. A Wait() that runs off the renderer does not delay it, so timing alone would miss it.
		var blockingCalls = FindBlockingCalls();
		report.Log(blockingCalls.Count is 0 ? "NewsPageBase never blocks on a task" : $"NewsPageBase blocks on a task with {string.Join(", ", blockingCalls)}");

		report.Expect("NewsPageBase never calls Wait(), Result or GetAwaiter().GetResult() on a task", "none", blockingCalls.Count is 0 ? "none" : string.Join(", ", blockingCalls), blockingCalls.Count is 0, _blockingHint);

		if (NewsPageSession.HasAsyncVoidMethods(report))
			return;

		// Part 2: load the page, then press Refresh and time how long the renderer takes to pick up a tiny piece of work, every 25 ms, until the refresh ends
		await using var session = new NewsPageSession(new FakeHackerNewsAPI(_responseTime));

		report.Log("Rendering the News page and waiting for it to finish initializing");
		await session.BeginRendering(token).ConfigureAwait(false);

		if (!await session.WaitForInitialization(token).ConfigureAwait(false))
		{
			report.Expect("The page finishes initializing", "within 10 seconds", "did not finish", false, "Step 1 checks this. Make it pass again first.");
			return;
		}

		report.Log("Pressing Refresh");

		var stopwatch = Stopwatch.StartNew();
		var refreshTask = session.PressRefresh();
		var longestDelay = TimeSpan.Zero;
		var heartbeats = 0;

		while (!refreshTask.IsCompleted && stopwatch.Elapsed < NewsPageSession.RenderTimeout)
		{
			var postedAt = stopwatch.Elapsed;
			var ranAt = await session.Dispatcher.InvokeAsync(() => stopwatch.Elapsed).WaitAsync(NewsPageSession.RenderTimeout, token).ConfigureAwait(false);
			var delay = ranAt - postedAt;

			if (delay > longestDelay)
			{
				longestDelay = delay;
				report.Log($"The renderer took {delay.TotalMilliseconds:0} ms to run a piece of work posted {postedAt.TotalMilliseconds:0} ms into the refresh");
			}

			heartbeats++;
			await Task.Delay(_heartbeatInterval, token).ConfigureAwait(false);
		}

		await refreshTask.WaitAsync(NewsPageSession.RenderTimeout, token).ConfigureAwait(false);
		var refreshTime = stopwatch.Elapsed;

		var isRefreshing = await session.Read(static page => page.IsRefreshing, token).ConfigureAwait(false);

		report.Log($"The refresh finished after {refreshTime.TotalSeconds:F2}s. The renderer ran {heartbeats} pieces of work, and the longest wait was {longestDelay.TotalMilliseconds:0} ms");

		report.Expect(
			"While a refresh runs, the renderer picks up other work quickly",
			$"within {_maximumRendererDelay.TotalMilliseconds:0} ms",
			$"{longestDelay.TotalMilliseconds:0} ms",
			longestDelay <= _maximumRendererDelay,
			"Blazor's renderer was blocked, so everything else for this browser tab had to wait. Something in the refresh holds the renderer's thread while it waits for a task, and the code after the awaits in RefreshAsync(CancellationToken) runs on the renderer. Await the task instead.");

		report.Expect(
			"Refresh keeps its indicator up for the 2 second minimum, then turns it off",
			"at least 2 seconds, then off",
			$"{refreshTime.TotalSeconds:F1} seconds, then {(isRefreshing ? "still on" : "off")}",
			refreshTime >= _minimumRefreshTime && !isRefreshing,
			refreshTime < _minimumRefreshTime
				? "The refresh ended before its 2 second minimum. Await the minimum refresh time rather than removing the wait."
				: "IsListRefreshing is still true after the refresh finished. Turn it off, through InvokeAsync(...), once the minimum refresh time has passed.");

		await session.ExpectNoErrors(report, token).ConfigureAwait(false);
	}

	// Blocking waits the compiler never generates. Every await compiles into one IsCompleted check and one GetResult() call on the same awaiter,
	// so an awaiter's GetResult() call without its own IsCompleted check was written by hand.
	static IReadOnlyList<string> FindBlockingCalls()
	{
		var calledMethods = CodeInspector.GetCalledMethods(typeof(NewsPageBase));
		List<string> blockingCalls = [];

		if (calledMethods.Any(static method => method is "Task.Wait" or "Task.WaitAll" or "Task.WaitAny"))
			blockingCalls.Add("Wait()");

		if (calledMethods.Any(static method => method is "Task`1.get_Result" or "ValueTask`1.get_Result"))
			blockingCalls.Add("Result");

		var awaiterTypes = calledMethods
			.Where(static method => method.EndsWith(".GetResult", StringComparison.Ordinal))
			.Select(static method => method[..^".GetResult".Length])
			.Where(static type => type.Contains("Awaiter", StringComparison.Ordinal))
			.Distinct();

		if (awaiterTypes.Any(awaiterType => calledMethods.Count(method => method == $"{awaiterType}.GetResult") > calledMethods.Count(method => method == $"{awaiterType}.get_IsCompleted")))
			blockingCalls.Add("GetAwaiter().GetResult()");

		return blockingCalls;
	}
}