using System.Collections.Frozen;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HackerNews;

partial class NewsViewModel : BaseViewModel
{
	readonly HackerNewsAPIService _hackerNewsApiService;
	readonly AsyncAwaitBestPractices.WeakEventManager _pullToRefreshEventManager = new();

	public NewsViewModel(IDispatcher dispatcher, HackerNewsAPIService hackerNewsApiService) : base(dispatcher)
	{
		_hackerNewsApiService = hackerNewsApiService;

		//ToDo Refactor
		Refresh(CancellationToken.None);
	}

	public event EventHandler<string> PullToRefreshFailed
	{
		add => _pullToRefreshEventManager.AddEventHandler(value);
		remove => _pullToRefreshEventManager.RemoveEventHandler(value);
	}
	
	
	[ObservableProperty]
	public partial bool IsListRefreshing { get; set; }

	[RelayCommand]
	async Task Refresh(CancellationToken token)
	{
		// ToDo Refactor
		var minimumRefreshTimeTask = Task.Delay(TimeSpan.FromSeconds(2));

		try
		{
			// ToDo Refactor
			var topStoriesList = await GetTopStories(token, StoriesConstants.NumberOfStories);

			TopStoryCollection.Clear();

			foreach (var story in topStoriesList)
			{
				if (!TopStoryCollection.Any(x => x.Title.Equals(story.Title, StringComparison.Ordinal)))
					InsertIntoSortedCollection(TopStoryCollection, (a, b) => b.Score.CompareTo(a.Score), story);
			}
		}
		catch (Exception e)
		{
			OnPullToRefreshFailed(e.ToString());
		}
		finally
		{
			// ToDo Refactor
			minimumRefreshTimeTask.Wait();
			IsListRefreshing = false;
		}
	}

	// ToDo Refactor
	async Task<FrozenSet<StoryModel>> GetTopStories(CancellationToken token, int storyCount = int.MaxValue)
	{
		List<StoryModel> topStoryList = [];

		var topStoryIds = await GetTopStoryIDs(token).ConfigureAwait(false);

		foreach (var topStoryId in topStoryIds)
		{
			var story = await GetStory(topStoryId, token).ConfigureAwait(false);
			topStoryList.Add(story);

			if (topStoryList.Count >= storyCount)
				break;
		}

		return topStoryList.OrderByDescending(x => x.Score).ToFrozenSet();
	}

	//ToDo Refactor
	async Task<StoryModel> GetStory(long storyId, CancellationToken token)
	{
		return await _hackerNewsApiService.GetStory(storyId, token);
	}

	//ToDo Refactor
	async Task<IReadOnlyList<long>> GetTopStoryIDs(CancellationToken token)
	{
		if (IsDataRecent(TimeSpan.FromHours(1)))
			return TopStoryCollection.Select(x => x.Id).ToList();

		try
		{
			return await _hackerNewsApiService.GetTopStoryIDs(token);
		}
		catch (Exception e)
		{
			Trace.WriteLine(e.Message);
			throw;
		}
	}

	bool IsDataRecent(TimeSpan timeSpan) => TopStoryCollection.Any() 
	                                        && (DateTimeOffset.UtcNow - TopStoryCollection.Max(x => x.CreatedAt)) > timeSpan;

	void OnPullToRefreshFailed(string message) => _pullToRefreshEventManager.RaiseEvent(this, message, nameof(PullToRefreshFailed));
}