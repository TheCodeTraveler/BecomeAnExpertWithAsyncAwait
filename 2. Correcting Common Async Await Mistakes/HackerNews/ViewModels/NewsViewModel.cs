using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HackerNews;

partial class NewsViewModel(HackerNewsAPIService hackerNewsApiService) : BaseViewModel
{
	readonly HackerNewsAPIService _hackerNewsApiService = hackerNewsApiService;

	[ObservableProperty]
	bool _isListRefreshing;

	public event EventHandler<string>? PullToRefreshFailed;

	public ObservableCollection<StoryModel> TopStoryCollection { get; } = [];

	static void InsertIntoSortedCollection<T>(ObservableCollection<T> collection, Comparison<T> comparison, T modelToInsert)
	{
		if (collection.Count is 0)
		{
			collection.Add(modelToInsert);
		}
		else
		{
			int index = 0;
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
	}

	[RelayCommand]
	async Task Refresh(CancellationToken token)
	{
		TopStoryCollection.Clear();

		try
		{
			await foreach (var story in GetTopStories(StoriesConstants.NumberOfStories, token).ConfigureAwait(false))
			{
				if (!TopStoryCollection.Any(x => x.Title.Equals(story.Title)))
					InsertIntoSortedCollection(TopStoryCollection, (a, b) => b.Score.CompareTo(a.Score), story);

				await Task.Yield();
			}
		}
		catch (Exception e)
		{
			PullToRefreshFailed?.Invoke(this, e.ToString());
		}
		finally
		{
			IsListRefreshing = false;
		}
	}

	async IAsyncEnumerable<StoryModel> GetTopStories(int storyCount, [EnumeratorCancellation] CancellationToken token)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(storyCount);

		var topStoryIds = await _hackerNewsApiService.GetTopStoryIDs(token).ConfigureAwait(false);

		var getTopStoryTaskList = topStoryIds.Select(id => _hackerNewsApiService.GetStory(id, token)).ToList();

		await foreach (var topStoryTask in getTopStoryTaskList.ToAsyncEnumerable().WithCancellation(token).ConfigureAwait(false))
		{
			var story = await topStoryTask.ConfigureAwait(false);
			yield return story;

			if (--storyCount <= 0)
			{
				break;
			}
		}
	}
}