namespace InternalsLab;

// Step 4: stands in for the Hacker News API from Correcting Common Async Await Mistakes, so this app runs offline.
// Every request waits a moment, like a real network call, except a request for a story that is already in the cache.
public sealed class SimulatedStoryFeed
{
	static readonly TimeSpan _networkDelay = TimeSpan.FromMilliseconds(150);

	static readonly IReadOnlyList<Story> _topStories =
	[
		new Story(1001, "Show HN: A tiny async state machine visualizer", 412),
		new Story(1002, "What ConfigureAwait(false) really does", 287),
		new Story(1003, "Ask HN: Does Blazor Server have a UI thread?", 164),
	];

	// Loading the front page already downloaded the top story
	// Try it: remove the top story from this cache and apply the change with Hot Reload. What does checkpoint 3 see now?
	readonly Dictionary<long, Story> _cache = new Dictionary<long, Story>
	{
		[_topStories[0].Id] = _topStories[0],
	};

	public async Task<IReadOnlyList<long>> GetTopStoryIDs(CancellationToken token)
	{
		await Task.Delay(_networkDelay, token).ConfigureAwait(false);

		return [.. _topStories.Select(static story => story.Id)];
	}

	// A cached story comes back in a ValueTask that has already completed, so awaiting it never has to wait
	public ValueTask<Story> GetStory(long storyId, CancellationToken token)
	{
		if (_cache.TryGetValue(storyId, out var cachedStory))
			return ValueTask.FromResult(cachedStory);

		return new ValueTask<Story>(DownloadStory(storyId, token));
	}

	static async Task<Story> DownloadStory(long storyId, CancellationToken token)
	{
		await Task.Delay(_networkDelay, token).ConfigureAwait(false);

		return _topStories.First(story => story.Id == storyId);
	}
}