using System.Diagnostics;
using System.Runtime.CompilerServices;
using AsyncAwaitBestPractices;
using Microsoft.AspNetCore.Components;

namespace HackerNews.Components.Pages;

public partial class NewsPageBase : ComponentBase, IDisposable
{
	readonly CancellationTokenSource _disposeCancellationTokenSource = new();

	[Inject]
	HackerNewsAPIService HackerNewsApiService { get; set; } = null!;

	protected List<StoryModel> TopStoryCollection { get; } = [];
	protected bool IsListRefreshing { get; set; }
	protected string? RefreshErrorMessage { get; set; }

	protected override void OnInitialized()
	{
		IsListRefreshing = true;

		//ToDo Refactor
		RefreshAsync(_disposeCancellationTokenSource.Token);
	}

	protected Task RefreshAsync() => RefreshAsync(_disposeCancellationTokenSource.Token);

	async Task RefreshAsync(CancellationToken token)
	{
		IsListRefreshing = true;
		RefreshErrorMessage = null;

		// ToDo Refactor
		var minimumRefreshTimeTask = Task.Delay(TimeSpan.FromSeconds(2));

		try
		{
			// ToDo Refactor
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
			await InvokeAsync(() => RefreshErrorMessage = e.ToString());
		}
		finally
		{
			// ToDo Refactor
			minimumRefreshTimeTask.Wait();

			await InvokeAsync(() =>
			{
				IsListRefreshing = false;
				StateHasChanged();
			});
		}
	}

	// ToDo Refactor
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

	//ToDo Refactor
	async Task<StoryModel> GetStory(long storyId, CancellationToken token)
	{
		return await HackerNewsApiService.GetStory(storyId, token);
	}

	//ToDo Refactor
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

	bool IsDataRecent(TimeSpan timeSpan) => TopStoryCollection.Any()
		&& (DateTimeOffset.UtcNow - TopStoryCollection.Max(x => x.CreatedAt)) < timeSpan;

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!disposing)
		{
			return;
		}

		_disposeCancellationTokenSource.Cancel();
		_disposeCancellationTokenSource.Dispose();
	}
}