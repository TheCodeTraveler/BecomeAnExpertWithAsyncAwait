namespace InternalsLab;

// Step 4: SynchronizationContext
// RefreshAsync() is shaped like RefreshAsync() in Correcting Common Async Await Mistakes, with a simulated story feed instead of Hacker News.
// The Step 4 page calls it straight from its Run the experiment click handler, so it starts on Blazor's renderer, like any event handler.
// Every RecordSynchronizationContext(...) call is one checkpoint on the Step 4 page. Predict SynchronizationContext.Current at each one before you run it.
public sealed class SynchronizationContextExperiment(CheckpointLog<string?> log)
{
	readonly SimulatedStoryFeed _storyFeed = new();

	// Stands in for the page's TopStoryCollection: component state, so only the renderer may change it
	public List<Story> TopStories { get; } = [];

	public async Task RefreshAsync(Func<Action, Task> invokeAsync, CancellationToken token)
	{
		// Step 4: checkpoint 1. Before the first await, still inside the click handler
		RecordSynchronizationContext(1);

		// Step 4: a plain await. GetTopStoryIDs() has to wait for the simulated network.
		var topStoryIds = await _storyFeed.GetTopStoryIDs(token);

		// Step 4: checkpoint 2. After a plain await
		RecordSynchronizationContext(2);

		// Step 4: the top story is already in the feed's cache, so the ValueTask has completed before the await looks at it
		var topStory = await _storyFeed.GetStory(topStoryIds[0], token).ConfigureAwait(false);

		// Step 4: checkpoint 3. After ConfigureAwait(false) on a story that was already cached
		RecordSynchronizationContext(3);

		// Step 4: this story is not cached, so the await has to wait for the simulated network
		// Try it: remove ConfigureAwait(false) from this await and apply the change with Hot Reload. Which checkpoints change?
		var secondStory = await _storyFeed.GetStory(topStoryIds[1], token).ConfigureAwait(false);

		// Step 4: checkpoint 4. After ConfigureAwait(false) on a story that had to download
		RecordSynchronizationContext(4);

		// Step 4: a plain await again, but it runs after checkpoint 4
		var thirdStory = await _storyFeed.GetStory(topStoryIds[2], token);

		// Step 4: checkpoint 5. After a plain await that started where checkpoint 4 left off
		RecordSynchronizationContext(5);

		// Step 4: InvokeAsync() runs the lambda through Blazor's renderer, which is how RefreshAsync() safely changes component state
		await invokeAsync(() =>
		{
			TopStories.AddRange([topStory, secondStory, thirdStory]);

			// Step 4: checkpoint 6. Inside InvokeAsync()
			RecordSynchronizationContext(6);
		});
	}

	void RecordSynchronizationContext(int checkpoint) => log.Record(checkpoint, SynchronizationContext.Current?.GetType().Name);
}