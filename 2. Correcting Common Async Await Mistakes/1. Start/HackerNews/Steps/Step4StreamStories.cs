using System.Diagnostics;
using HackerNews.Components.Pages;

namespace HackerNews;

public sealed class Step4StreamStories : WorkshopStep
{
	const string _notStreamingHint = "The page showed nothing until the very last story arrived, even though Hacker News had already answered for every other story. "
		+ "Hand each story to the page as soon as it arrives, add it to the list in its sorted place through InvokeAsync(...), and render it.";

	// The front page's last story is the one Hacker News is slow to return
	static readonly long _slowStoryId = FakeHackerNewsAPI.GetStoryId(StoriesConstants.NumberOfStories);

	// Streaming renders a story a few milliseconds after it arrives. This is how long the step looks for one before deciding nothing streamed.
	static readonly TimeSpan _renderWait = TimeSpan.FromSeconds(1);

	static readonly TimeSpan _responseTime = TimeSpan.FromMilliseconds(10);

	public override int Number => 4;

	public override string Scenario => "Stream stories as they arrive";

	public override string Title => "GetTopStories() and RefreshAsync(CancellationToken)";

	public override string Story => "Hacker News serves every story as its own request, so the page makes 50 of them. GetTopStories() collects every story into a list, sorts it, and only then hands it back, "
		+ "so people stare at the loading skeleton until the slowest story arrives, even though most of the others came back long before. "
		+ "The page already knows how to put a single story in its sorted place. It just never gets a story until it has all of them.";

	public override string SeeItInTheApp => "Reload the Top stories page in your browser and watch the count of loaded stories in the orange bar. It sits at 0 behind the loading skeleton, then jumps straight to 50. "
		+ "Once the stories stream, it counts up as they arrive. This step makes the last story slow, and looks at the page while it waits.";

	public override string FileToChange => "Components/Pages/News.razor.cs";

	public override IReadOnlyList<string> Tasks { get; } =
	[
		"Stream the stories out of GetTopStories() as an IAsyncEnumerable<StoryModel>, handing each story back as soon as it arrives.",
		"Give the stream the CancellationToken as well, so a canceled refresh stops the stream. [EnumeratorCancellation] connects the token passed to WithCancellation() to the iterator.",
		"In RefreshAsync(CancellationToken), clear the list once, then add each story to its sorted place through InvokeAsync(...), and render it as it arrives.",
		"Keep the list sorted by score, and keep every story in it once.",
	];

	public override IReadOnlyList<string> Clues { get; } =
	[
		"Story loading waits for a full list before the UI can process results.",
		"An async iterator is an async method that can both await and yield return.",
		"await foreach consumes an IAsyncEnumerable<T>, and ConfigureAwait(false) works on it too.",
		"InsertIntoSortedList() already puts one story in its place, so nothing needs sorting at the end.",
	];

	public override string TimeoutHint => "The story stream never finished. Check that GetTopStories() completes after its last story, and that the loop in RefreshAsync(CancellationToken) ends.";

	public override async Task Run(StepContext context)
	{
		var report = context.Report;
		var token = context.Token;

		// Part 1: read the compiled page for the stream's return type
		var getTopStories = CodeInspector.FindMethod(typeof(NewsPageBase), "GetTopStories");
		var returnType = getTopStories?.ReturnType;

		report.Expect(
			"GetTopStories() returns IAsyncEnumerable<StoryModel>",
			"IAsyncEnumerable<StoryModel>",
			returnType is null ? "no GetTopStories() method" : CodeInspector.DescribeType(returnType),
			returnType == typeof(IAsyncEnumerable<StoryModel>),
			returnType is null
				? "Keep the method's name, GetTopStories, so this step can find it."
				: "A method that returns a list can only return once, when every story is in it. An async iterator returns IAsyncEnumerable<StoryModel> and can hand back one story at a time.");

		if (NewsPageSession.HasAsyncVoidMethods(report))
			return;

		// Part 2: load the page with the front page's last story held back, and look at the page while every other story has arrived
		var hackerNewsApi = new FakeHackerNewsAPI(_responseTime) { HeldStoryId = _slowStoryId };
		await using var session = new NewsPageSession(hackerNewsApi);

		try
		{
			report.Log($"Rendering the News page. Every Hacker News call takes {_responseTime.TotalMilliseconds:0} ms, except story {StoriesConstants.NumberOfStories}, which waits until the step has looked at the page");
			await session.BeginRendering(token).ConfigureAwait(false);

			await hackerNewsApi.EveryOtherStoryReturned.WaitAsync(NewsPageSession.RenderTimeout, token).ConfigureAwait(false);
			report.Log($"Hacker News has returned {hackerNewsApi.StoriesReturned} stories. Story {StoriesConstants.NumberOfStories} is still on its way");

			var storiesOnPage = 0;
			var stopwatch = Stopwatch.StartNew();

			while (storiesOnPage is 0 && stopwatch.Elapsed < _renderWait)
			{
				storiesOnPage = NewsPageSession.CountStoriesOnPage(await session.GetHtml(token).ConfigureAwait(false));

				if (storiesOnPage is 0)
					await Task.Delay(TimeSpan.FromMilliseconds(20), token).ConfigureAwait(false);
			}

			report.Log($"While story {StoriesConstants.NumberOfStories} is on its way, the page shows {storiesOnPage} stories");

			report.Expect(
				"While the last story is on its way, the page already shows the stories that have arrived",
				"at least 1 story on the page",
				$"{NewsPageSession.DescribeStories(storiesOnPage)} on the page",
				storiesOnPage > 0,
				_notStreamingHint);
		}
		finally
		{
			hackerNewsApi.ReleaseHeldStory();
		}

		if (!await session.WaitForInitialization(token).ConfigureAwait(false))
		{
			report.Expect("The page finishes initializing", "within 10 seconds", "did not finish", false, TimeoutHint);
			return;
		}

		// Part 3: once the last story arrives, the list is complete, sorted, and has no story twice
		var stories = await session.Read(static page => page.Stories, token).ConfigureAwait(false);
		var isSorted = stories.Zip(stories.Skip(1)).All(static pair => pair.First.Score >= pair.Second.Score);
		var distinctStories = stories.DistinctBy(static story => story.Id).Count();

		report.Log($"The refresh finished with {stories.Count} stories, {(isSorted ? "sorted" : "not sorted")} by score, {distinctStories} of them different");

		report.Expect(
			"When the refresh finishes, the page lists every story once, highest score first",
			$"{StoriesConstants.NumberOfStories} stories, sorted, none twice",
			$"{NewsPageSession.DescribeStories(stories.Count)}, {(isSorted ? "sorted" : "not sorted")}, {(distinctStories == stories.Count ? "none twice" : $"{stories.Count - distinctStories} twice")}",
			stories.Count is StoriesConstants.NumberOfStories && isSorted && distinctStories == stories.Count,
			"Stories now arrive one at a time and in any order. Clear the list once before the first story, insert each story into its sorted place, and skip a story that is already in the list.");

		await session.ExpectNoErrors(report, token).ConfigureAwait(false);
	}
}