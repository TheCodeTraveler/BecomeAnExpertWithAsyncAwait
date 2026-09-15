using System.Diagnostics;
using System.Runtime.CompilerServices;
using AsyncAwaitBestPractices;
using Microsoft.AspNetCore.Components;

namespace HackerNews.Components.Pages;

public partial class NewsPageBase : ComponentBase, IDisposable
{
	readonly CancellationTokenSource _disposeCancellationTokenSource = new();

	[Inject]
	public required HackerNewsAPIService HackerNewsApiService { get; init; }

	[Inject]
	public required ILogger<NewsPageBase> Logger { get; init; }

	protected List<StoryModel> TopStoryCollection { get; } = [];
	protected bool IsListRefreshing { get; set; }
	protected string? RefreshErrorMessage { get; set; }

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected static void InsertIntoSortedList<T>(List<T> collection, Comparison<T> comparison, T modelToInsert)
	{
		if (collection.Count is 0)
		{
			collection.Add(modelToInsert);
			return;
		}

		var index = 0;
		foreach (var model in collection)
		{
			if (comparison(model, modelToInsert) >= 0)
			{
				collection.Insert(index, modelToInsert);
				return;
			}

			index++;
		}

		collection.Insert(index, modelToInsert);
	}

	protected override void OnInitialized()
	{
		IsListRefreshing = true;

		// ToDo Refactor (Step 1): this starts the refresh and forgets it. OnInitialized() returns straight away,
		// so Blazor treats the page as initialized while the stories are still loading, and nothing observes the task.
		RefreshAsync(_disposeCancellationTokenSource.Token);
	}

	protected Task RefreshAsync() => RefreshAsync(_disposeCancellationTokenSource.Token);

	protected virtual void Dispose(bool disposing)
	{
		if (!disposing)
		{
			return;
		}

		_disposeCancellationTokenSource.Cancel();
		_disposeCancellationTokenSource.Dispose();
	}

	async Task RefreshAsync(CancellationToken token)
	{
		IsListRefreshing = true;
		RefreshErrorMessage = null;

		// ToDo Refactor (Step 3): this method receives a CancellationToken, and this delay never hears about it.
		// When the page goes away, the refresh keeps running until the 2 seconds are up.
		var minimumRefreshTimeTask = Task.Delay(TimeSpan.FromSeconds(2));

		try
		{
			// ToDo Refactor (Step 2): this await captures Blazor's context, so everything after it, including the
			// finally block, runs on the renderer.
			// ToDo Refactor (Step 4): it also hands back nothing until every story has arrived.
			var topStoriesList = await GetTopStories(token, StoriesConstants.NumberOfStories);

			await InvokeAsync(() =>
			{
				TopStoryCollection.Clear();

				foreach (var story in topStoriesList)
				{
					if (!TopStoryCollection.Any(x => x.Title.Equals(story.Title, StringComparison.Ordinal)))
					{
						InsertIntoSortedList(TopStoryCollection, (a, b) => b.Score.CompareTo(a.Score), story);
					}
				}
			});
		}
		catch (Exception e)
		{
			Logger.LogError(e, "Failed to refresh Hacker News top stories.");
			await InvokeAsync(() => RefreshErrorMessage = "Unable to refresh top stories. Check your connection and try again.");
		}
		finally
		{
			// ToDo Refactor (Step 2): Wait() blocks the thread that runs it. Here that is Blazor's renderer for this
			// browser tab, and nothing else can run on it until the 2 second delay ends.
			minimumRefreshTimeTask.Wait();

			await InvokeAsync(() =>
			{
				IsListRefreshing = false;
				StateHasChanged();
			});
		}
	}

	// ToDo Refactor (Step 4): the page sees none of these stories until the last one has arrived and the list is sorted
	async Task<IReadOnlyList<StoryModel>> GetTopStories(CancellationToken token, int storyCount = int.MaxValue)
	{
		List<StoryModel> topStoryList = [];

		var topStoryIds = await GetTopStoryIDs(token).ConfigureAwait(false);

		foreach (var topStoryId in topStoryIds)
		{
			var story = await GetStory(topStoryId, token).ConfigureAwait(false);
			topStoryList.Add(story);

			if (topStoryList.Count >= storyCount)
			{
				break;
			}
		}

		return topStoryList.OrderByDescending(x => x.Score).ToList();
	}

	// ToDo Refactor (Step 5): an async state machine whose only job is to await one task and return its result
	async Task<StoryModel> GetStory(long storyId, CancellationToken token)
	{
		return await HackerNewsApiService.GetStory(storyId, token);
	}

	// ToDo Refactor (Step 5): when the stories on the page are recent, this completes without awaiting anything,
	// yet every call still allocates a Task to hand back the result
	async Task<IReadOnlyList<long>> GetTopStoryIDs(CancellationToken token)
	{
		if (IsDataRecent(TimeSpan.FromHours(1)))
		{
			return TopStoryCollection.Select(x => x.Id).ToList();
		}

		try
		{
			return await HackerNewsApiService.GetTopStoryIDs(token);
		}
		catch (Exception e)
		{
			Trace.WriteLine(e.Message);
			throw;
		}
	}

	bool IsDataRecent(TimeSpan timeSpan) => TopStoryCollection.Any()
		&& (DateTimeOffset.UtcNow - TopStoryCollection.Max(x => x.CreatedAt)) < timeSpan;
}