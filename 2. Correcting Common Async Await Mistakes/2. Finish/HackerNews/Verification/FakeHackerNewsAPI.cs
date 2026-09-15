namespace HackerNews;

// Workshop plumbing: an in-memory Hacker News for the steps, so every check runs offline and gives the same answer on every machine.
// Every call waits a delay the step chooses and honors its CancellationToken, the way a real HTTP request does.
public sealed class FakeHackerNewsAPI(TimeSpan responseTime) : IHackerNewsAPI
{
	// More than the page asks for, like the real API
	public const int TopStoryCount = 80;

	// What the fake puts in the exception when it is failing. None of it may ever reach the page.
	public const string FailureDetail = "Response status code does not indicate success: 503 (Service Unavailable) from hacker-news.firebaseio.com edge node sjc-7";

	readonly TaskCompletionSource _heldStoryReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
	readonly TaskCompletionSource _everyOtherStoryReturned = new(TaskCreationOptions.RunContinuationsAsynchronously);
	readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

	int _topStoryIdRequests;
	int _storyRequests;
	int _storiesReturned;

	// When set, every call throws HttpRequestException, the way it does when Hacker News is down
	public bool IsFailing { get; init; }

	// When set, this story is not returned until ReleaseHeldStory() is called, so a step can look at the page while it waits
	public long? HeldStoryId { get; init; }

	public int TopStoryIdRequests => Volatile.Read(ref _topStoryIdRequests);

	public int StoryRequests => Volatile.Read(ref _storyRequests);

	public int StoriesReturned => Volatile.Read(ref _storiesReturned);

	// Completes once every story the page shows, apart from the held one, has been returned
	public Task EveryOtherStoryReturned => _everyOtherStoryReturned.Task;

	// Ranks start at 1, the way the front page numbers them
	public static long GetStoryId(int rank) => 45_000_000 + rank;

	// Scores are deliberately out of rank order, so the page has to sort them. 37 and 101 share no factors, so no two ranks get the same score.
	public static int GetScore(int rank) => 100 + (rank * 37 % 101);

	public void ReleaseHeldStory() => _heldStoryReleased.TrySetResult();

	public async Task<IReadOnlyList<long>> GetTopStoryIDs(CancellationToken token)
	{
		Interlocked.Increment(ref _topStoryIdRequests);

		await Task.Delay(responseTime, token).ConfigureAwait(false);

		if (IsFailing)
			throw new HttpRequestException(FailureDetail);

		return [.. Enumerable.Range(1, TopStoryCount).Select(GetStoryId)];
	}

	public async Task<StoryModel> GetStory(long storyId, CancellationToken token)
	{
		Interlocked.Increment(ref _storyRequests);

		await Task.Delay(responseTime, token).ConfigureAwait(false);

		if (IsFailing)
			throw new HttpRequestException(FailureDetail);

		if (storyId == HeldStoryId)
			await _heldStoryReleased.Task.WaitAsync(token).ConfigureAwait(false);

		var rank = (int)(storyId - GetStoryId(0));

		if (Interlocked.Increment(ref _storiesReturned) is StoriesConstants.NumberOfStories - 1 && HeldStoryId is not null)
			_everyOtherStoryReturned.TrySetResult();

		// Posted between 1 and 80 minutes ago, so the stories on the page always count as recent
		return new StoryModel(
			"workshop",
			storyId,
			GetScore(rank),
			_now.AddMinutes(-rank).ToUnixTimeSeconds(),
			$"Workshop story {rank}",
			"story",
			$"https://example.com/stories/{rank}");
	}
}