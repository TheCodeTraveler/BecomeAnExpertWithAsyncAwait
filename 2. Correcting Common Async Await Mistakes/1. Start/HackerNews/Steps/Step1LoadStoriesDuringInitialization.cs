using HackerNews.Components.Pages;

namespace HackerNews;

public sealed class Step1LoadStoriesDuringInitialization : WorkshopStep
{
	const string _asyncVoidHint = "async void silences the compiler warning without fixing anything. Nobody can await an async void method, and an exception that escapes one cannot be caught by its caller: "
		+ "on Blazor's renderer it tears down the user's connection, and anywhere else it ends the whole app. Blazor's lifecycle methods can return a Task, so use one that does.";

	const string _notInitializedHint = "Blazor only waits for work it can see through the Task a lifecycle method returns. OnInitialized() starts the refresh and returns straight away, so Blazor finished initializing the page before a single story arrived. "
		+ "Make the refresh part of the page's initialization, so the Task Blazor gets back completes only after the stories load.";

	// Fast enough that loading the page takes about as long as the refresh's 2 second minimum
	static readonly TimeSpan _responseTime = TimeSpan.FromMilliseconds(10);

	public override int Number => 1;

	public override string Scenario => "Load stories as part of initialization";

	public override string Title => "OnInitialized()";

	public override string Story => "Blazor waits for a page to finish initializing before it treats that page's first render as complete. Prerendering sends that render to the browser, and HtmlRenderer, which renders components into emails, PDFs and caches, returns it. "
		+ "The Top stories page starts its refresh in OnInitialized() and returns straight away, so as far as Blazor knows, the page finished initializing with an empty story list. "
		+ "Nothing awaits the refresh either, so nothing can tell when it ends, or whether it failed.";

	public override string SeeItInTheApp => "Build the app and read the CS4014 warning on OnInitialized(): the compiler has already spotted this one. The Top stories page itself looks fine, because this app turns prerendering off, so the browser simply shows the loading skeleton until the stories arrive. "
		+ "This step renders the page the way prerendering does, waiting for initialization to finish, and today that render is the loading skeleton.";

	public override string FileToChange => "Components/Pages/News.razor.cs";

	public override IReadOnlyList<string> Tasks { get; } =
	[
		"Do not leave an unobserved task in the page's initialization. The Task Blazor gets back from initialization should complete only after the stories load.",
		"Do not reach for async void to silence the warning. Use it only when a framework API truly requires it, and Blazor's lifecycle methods do not.",
		"Use safe fire-and-forget, such as SafeFireAndForget() from AsyncAwaitBestPractices, only when the caller truly cannot return a Task. A Blazor lifecycle method can.",
	];

	public override IReadOnlyList<string> Clues { get; } =
	[
		"A task is started from a lifecycle method without being awaited. That is what CS4014 is pointing at.",
		"Blazor only knows about work it can see through the Task a lifecycle method returns.",
		"Most of Blazor's synchronous lifecycle methods have an async sibling that returns a Task, and Blazor awaits it.",
	];

	public override string TimeoutHint => "Your News page never finished initializing. Check that nothing in OnInitialized() or OnInitializedAsync() blocks on a task with Wait(), Result or GetAwaiter().GetResult(). The refresh needs Blazor's renderer to finish, and a blocking wait on the renderer holds it forever.";

	public override async Task Run(StepContext context)
	{
		var report = context.Report;
		var token = context.Token;

		// Part 1: read the compiled page for async void methods. The page is only rendered once there are none, because an exception escaping one can end the app.
		var asyncVoidMethods = CodeInspector.GetAsyncVoidMethods(typeof(NewsPageBase));
		report.Log(asyncVoidMethods.Count is 0 ? "NewsPageBase has no async void methods" : $"NewsPageBase has async void methods: {string.Join(", ", asyncVoidMethods)}");

		report.Expect("NewsPageBase has no async void methods", "none", asyncVoidMethods.Count is 0 ? "none" : string.Join(", ", asyncVoidMethods), asyncVoidMethods.Count is 0, _asyncVoidHint);

		if (asyncVoidMethods.Count is not 0)
		{
			report.Expect($"When Blazor finishes initializing the page, all {StoriesConstants.NumberOfStories} stories are loaded", $"{StoriesConstants.NumberOfStories} stories", "Skipped: the page has async void methods", false, _asyncVoidHint);
			return;
		}

		// Part 2: render the page against an in-memory Hacker News and wait for Blazor to finish initializing it, the way prerendering does
		await using var session = new NewsPageSession(new FakeHackerNewsAPI(_responseTime));

		report.Log($"Rendering the News page. Every Hacker News call takes {_responseTime.TotalMilliseconds:0} ms");
		await session.BeginRendering(token).ConfigureAwait(false);

		report.Log("Waiting for Blazor to finish initializing the page");
		var isInitialized = await session.WaitForInitialization(token).ConfigureAwait(false);

		if (!isInitialized)
		{
			report.Log($"Initialization did not finish within {NewsPageSession.RenderTimeout.TotalSeconds:0} seconds");
			report.Expect($"When Blazor finishes initializing the page, all {StoriesConstants.NumberOfStories} stories are loaded", $"{StoriesConstants.NumberOfStories} stories", $"initialization did not finish within {NewsPageSession.RenderTimeout.TotalSeconds:0} seconds", false, TimeoutHint);
			return;
		}

		// Read everything at the instant initialization finished, before any fire-and-forget work can catch up
		var storiesLoaded = await session.Read(static page => page.Stories.Count, token).ConfigureAwait(false);
		var html = await session.GetHtml(token).ConfigureAwait(false);
		var storiesOnPage = NewsPageSession.CountStoriesOnPage(html);
		var storiesReturned = session.HackerNewsApi.StoriesReturned;
		var refreshFailed = session.Logger.Errors.Count > 0 || await session.Read(static page => page.ErrorMessage is not null, token).ConfigureAwait(false);
		var storiesHint = refreshFailed ? "The refresh failed while the page was initializing, so the stories never finished loading. The last expected result says what your page logged." : _notInitializedHint;

		report.Log($"Initialization finished. Hacker News had returned {storiesReturned} stories, the page had loaded {storiesLoaded}, and its render shows {storiesOnPage}");

		report.Expect(
			$"When Blazor finishes initializing the page, all {StoriesConstants.NumberOfStories} stories are loaded",
			NewsPageSession.DescribeStories(StoriesConstants.NumberOfStories),
			NewsPageSession.DescribeStories(storiesLoaded),
			storiesLoaded is StoriesConstants.NumberOfStories,
			storiesHint);

		report.Expect(
			"The page Blazor renders at that moment, which prerendering would send, lists every story",
			$"{NewsPageSession.DescribeStories(StoriesConstants.NumberOfStories)} on the page",
			$"{NewsPageSession.DescribeStories(storiesOnPage)} on the page",
			storiesOnPage is StoriesConstants.NumberOfStories,
			refreshFailed ? storiesHint : "Prerendering and HtmlRenderer send the page as it is rendered once initialization finishes. Until the refresh is part of initialization, that render is the loading skeleton.");

		await session.ExpectNoErrors(report, token).ConfigureAwait(false);
	}
}