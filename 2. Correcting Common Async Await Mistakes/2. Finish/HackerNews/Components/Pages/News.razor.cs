using System.Diagnostics;
using System.Runtime.CompilerServices;
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

	protected override async Task OnInitializedAsync()
	{
		IsListRefreshing = true;
		await RefreshAsync(_disposeCancellationTokenSource.Token);
	}

	protected Task RefreshAsync() => RefreshAsync(_disposeCancellationTokenSource.Token);

	async Task RefreshAsync(CancellationToken token)
	{
		IsListRefreshing = true;
		RefreshErrorMessage = null;
		await InvokeAsync(StateHasChanged);

		var minimumRefreshTimeTask = Task.Delay(TimeSpan.FromSeconds(2), token);

		try
		{
			var topStoryIds = await GetTopStoryIDs(token).ConfigureAwait(false);

			await InvokeAsync(TopStoryCollection.Clear);

			await foreach (var story in GetTopStories(topStoryIds, StoriesConstants.NumberOfStories, token).ConfigureAwait(false))
			{
				await InvokeAsync(() =>
				{
					if (!TopStoryCollection.Any(x => x.Title.Equals(story.Title, StringComparison.Ordinal)))
					{
						InsertIntoSortedList(TopStoryCollection, (a, b) => b.Score.CompareTo(a.Score), story);
					}

					StateHasChanged();
				});
			}
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
		}
		catch (Exception e)
		{
			Logger.LogError(e, "Failed to refresh Hacker News top stories.");
			await InvokeAsync(() => RefreshErrorMessage = "Unable to refresh top stories. Check your connection and try again.");
		}
		finally
		{
			try
			{
				await minimumRefreshTimeTask.ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
			}

			if (!token.IsCancellationRequested)
			{
				await InvokeAsync(() =>
				{
					IsListRefreshing = false;
					StateHasChanged();
				});
			}
		}
	}

	async IAsyncEnumerable<StoryModel> GetTopStories(IReadOnlyList<long> topStoryIds, int storyCount, [EnumeratorCancellation] CancellationToken token)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(storyCount);

		var storyIds = topStoryIds.Take(storyCount).ToList();
		var getTopStoryTaskList = storyIds.Select(id => GetStory(id, token)).ToList();

		foreach (var topStoryTask in getTopStoryTaskList)
		{
			token.ThrowIfCancellationRequested();

			yield return await topStoryTask.ConfigureAwait(false);
		}
	}

	Task<StoryModel> GetStory(long storyId, CancellationToken token) => HackerNewsApiService.GetStory(storyId, token);

	async ValueTask<IReadOnlyList<long>> GetTopStoryIDs(CancellationToken token)
	{
		if (IsDataRecent(TimeSpan.FromHours(1)))
		{
			return TopStoryCollection.Select(x => x.Id).ToList();
		}

		try
		{
			return await HackerNewsApiService.GetTopStoryIDs(token).ConfigureAwait(false);
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