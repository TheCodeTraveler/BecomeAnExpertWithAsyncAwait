using System.Reflection;
using System.Runtime.CompilerServices;
using HackerNews.Components.Pages;

namespace HackerNews;

public sealed class Step5SkipUnneededStateMachines : WorkshopStep
{
	static readonly TimeSpan _responseTime = TimeSpan.FromMilliseconds(1);

	public override int Number => 5;

	public override string Scenario => "Skip the state machines you don't need";

	public override string Title => "GetStory() and GetTopStoryIDs()";

	public override string Story => "Every async method compiles into a state machine, and a call that does not finish synchronously allocates one. GetStory() only awaits one call and returns its result, "
		+ "so its state machine does nothing but cost, 50 times per refresh, on every page, for every user. "
		+ "GetTopStoryIDs() is the opposite: when the stories on the page are recent, it answers from them without awaiting anything, yet every call still allocates a new Task to hand back a result it already had.";

	public override string SeeItInTheApp => "Nothing on the page changes in this step, which is exactly why these costs survive code review. Press Refresh twice on the Top stories page: "
		+ "the second refresh reuses the story IDs already on the page instead of asking Hacker News, so GetTopStoryIDs() completes synchronously and still allocates a Task.";

	public override string FileToChange => "Components/Pages/News.razor.cs";

	public override IReadOnlyList<string> Tasks { get; } =
	[
		"Return Task directly from GetStory(), a method that only wraps another task, instead of awaiting that task in an async method.",
		"Return ValueTask<IReadOnlyList<long>> from GetTopStoryIDs(), because its hot path completes synchronously on most refreshes.",
		"Use ValueTask only where the hot path can complete synchronously. GetStory() always waits for the network, so it stays a Task.",
		"Keep the cached path in GetTopStoryIDs() synchronous: when the stories on the page are recent, it must not call Hacker News.",
		"Where GetTopStoryIDs() does await, ask whether the code after it needs Blazor's context.",
	];

	public override IReadOnlyList<string> Clues { get; } =
	[
		"Some methods create async state machines even though they only wrap another async call.",
		"A method whose only await is the task it returns, with no try, using or code after it, does not need to be async.",
		"ValueTask<T> avoids allocating when the result is already available. A ValueTask may only be awaited once.",
		"Changing a method's return type changes how its callers consume it.",
	];

	public override string TimeoutHint => "GetTopStoryIDs() never answered. Check that its cached path returns without waiting for anything.";

	public override async Task Run(StepContext context)
	{
		var report = context.Report;
		var token = context.Token;

		// Part 1: read the compiled page. The compiler marks every async method with AsyncStateMachineAttribute.
		var getStory = FindMethod("GetStory", typeof(long), typeof(CancellationToken));
		var getTopStoryIds = FindMethod("GetTopStoryIDs", typeof(CancellationToken));

		report.Log(getStory is null ? "NewsPageBase has no GetStory(long, CancellationToken)" : $"GetStory() returns {CodeInspector.DescribeType(getStory.ReturnType)}{(IsAsync(getStory) ? " and is an async method" : string.Empty)}");
		report.Log(getTopStoryIds is null ? "NewsPageBase has no GetTopStoryIDs(CancellationToken)" : $"GetTopStoryIDs() returns {CodeInspector.DescribeType(getTopStoryIds.ReturnType)}");

		report.Expect(
			"GetStory() returns Task<StoryModel> without an async state machine",
			"Task<StoryModel>, not async",
			getStory is null ? "no GetStory(long, CancellationToken) method" : $"{CodeInspector.DescribeType(getStory.ReturnType)}, {(IsAsync(getStory) ? "async" : "not async")}",
			getStory is not null && getStory.ReturnType == typeof(Task<StoryModel>) && !IsAsync(getStory),
			getStory is null
				? "Keep GetStory(long, CancellationToken), so this step can find it."
				: getStory.ReturnType != typeof(Task<StoryModel>)
					? "GetStory() always waits for the network, so a ValueTask would never complete synchronously and would only add cost. Keep returning Task<StoryModel>."
					: "GetStory() awaits a task and returns its result, and that is all. Its caller can await the same task, so hand it back directly and let the compiler skip the state machine.");

		report.Expect(
			"GetTopStoryIDs() returns ValueTask<IReadOnlyList<long>>",
			"ValueTask<IReadOnlyList<long>>",
			getTopStoryIds is null ? "no GetTopStoryIDs(CancellationToken) method" : CodeInspector.DescribeType(getTopStoryIds.ReturnType),
			getTopStoryIds?.ReturnType == typeof(ValueTask<IReadOnlyList<long>>),
			getTopStoryIds is null
				? "Keep GetTopStoryIDs(CancellationToken), so this step can find it."
				: "When the stories on the page are recent, GetTopStoryIDs() already has its answer, yet a Task<T> still has to be allocated to carry it. ValueTask<T> carries a result that is already available without allocating.");

		if (getTopStoryIds is null || NewsPageSession.HasAsyncVoidMethods(report))
			return;

		// Part 2: load the page, then call GetTopStoryIDs() on Blazor's renderer, the way a second refresh does, and see whether it needed Hacker News
		await using var session = new NewsPageSession(new FakeHackerNewsAPI(_responseTime));

		report.Log("Rendering the News page and waiting for it to finish initializing");
		await session.BeginRendering(token).ConfigureAwait(false);

		if (!await session.WaitForInitialization(token).ConfigureAwait(false))
		{
			report.Expect("The page finishes initializing", "within 10 seconds", "did not finish", false, "Step 1 checks this. Make it pass again first.");
			return;
		}

		var requestsBefore = session.HackerNewsApi.TopStoryIdRequests;
		report.Log("The page has loaded its stories. Calling GetTopStoryIDs() the way a second refresh does");

		var (completedSynchronously, storyIds) = await session.Read(page => CallGetTopStoryIds(getTopStoryIds, page), token).ConfigureAwait(false);
		var storyIdCount = storyIds is null ? 0 : (await storyIds.WaitAsync(NewsPageSession.RenderTimeout, token).ConfigureAwait(false)).Count;
		var requestsDuringCall = session.HackerNewsApi.TopStoryIdRequests - requestsBefore;

		report.Log($"GetTopStoryIDs() {(completedSynchronously ? "completed synchronously" : "did not complete synchronously")}, returned {storyIdCount} IDs, and asked Hacker News {requestsDuringCall} times");

		report.Expect(
			"With recent stories on the page, GetTopStoryIDs() completes synchronously without calling Hacker News",
			"completed synchronously, 0 calls",
			$"{(completedSynchronously ? "completed synchronously" : "did not complete synchronously")}, {requestsDuringCall} calls",
			completedSynchronously && requestsDuringCall is 0,
			"The cached path is the reason GetTopStoryIDs() can return a ValueTask. When IsDataRecent() is true, return the IDs of the stories on the page without awaiting anything.");
	}

	static MethodInfo? FindMethod(string name, params Type[] parameterTypes) =>
		typeof(NewsPageBase).GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, parameterTypes);

	static bool IsAsync(MethodInfo method) => method.IsDefined(typeof(AsyncStateMachineAttribute), false);

	// Runs on Blazor's renderer. Whatever GetTopStoryIDs() returns, report whether it had already completed, and hand back a Task to await for its result.
	static (bool CompletedSynchronously, Task<IReadOnlyList<long>>? StoryIds) CallGetTopStoryIds(MethodInfo getTopStoryIds, NewsPageProbe page) =>
		getTopStoryIds.Invoke(page, [CancellationToken.None]) switch
		{
			ValueTask<IReadOnlyList<long>> valueTask => (valueTask.IsCompleted, valueTask.AsTask()),
			Task<IReadOnlyList<long>> task => (task.IsCompleted, task),
			_ => (false, null),
		};
}