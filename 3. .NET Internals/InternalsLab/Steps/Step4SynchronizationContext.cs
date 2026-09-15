namespace InternalsLab;

// This file holds the answers to Step 4. Run the experiment and answer the questions on the page before you read it.
public sealed class Step4SynchronizationContext : WorkshopStep
{
	const string _contextColumn = "context";
	const string _rendererContext = "renderer";
	const string _noContext = "none";
	const string _aspNetContext = "aspnet";
	const string _otherContext = "other";

	public override int Number => 4;

	public override string Scenario => "SynchronizationContext";

	public override string Title => "SynchronizationContext.Current, ConfigureAwait(false), InvokeAsync(...)";

	public override string Story => "Steps 2 and 3 showed the values that ride on ExecutionContext. await captures one more piece of ambient state: SynchronizationContext.Current, the place it posts the continuation back to. "
		+ "In Blazor Server that is the renderer's RendererSynchronizationContext, which is what makes it safe to change component state, and it is exactly what ConfigureAwait(false) opts out of. "
		+ "The experiment, RefreshAsync() in [Experiments/SynchronizationContextExperiment.cs](Experiments/SynchronizationContextExperiment.cs#public async Task RefreshAsync), is a refresh shaped like RefreshAsync() from Correcting Common Async Await Mistakes, with a simulated story feed instead of Hacker News. "
		+ "Run the experiment calls it straight from this page's click handler, so it starts on Blazor's renderer. Predict SynchronizationContext.Current at every checkpoint.";

	public override int RecommendedMinutes => 10;

	public override string ExperimentFile => "Experiments/SynchronizationContextExperiment.cs";

	public override IReadOnlyList<string> TryIt { get; } =
	[
		"Remove ConfigureAwait(false) from the await before checkpoint 4 in [SynchronizationContextExperiment.cs](Experiments/SynchronizationContextExperiment.cs#Try it: remove ConfigureAwait) and apply the change with Hot Reload, then click Run it again. Which checkpoints change?",
		"Remove the top story from the cache in [SimulatedStoryFeed.cs](Experiments/SimulatedStoryFeed.cs#Try it: remove the top story) and apply the change with Hot Reload. What does checkpoint 3 see now?",
	];

	public override IReadOnlyList<ExperimentCheckpoint> Checkpoints { get; } =
	[
		new ExperimentCheckpoint(1, "1. Before the first await, in the click handler"),
		new ExperimentCheckpoint(2, "2. After a plain await on GetTopStoryIDs()"),
		new ExperimentCheckpoint(3, "3. After ConfigureAwait(false) on a story that was already cached"),
		new ExperimentCheckpoint(4, "4. After ConfigureAwait(false) on a story that had to download"),
		new ExperimentCheckpoint(5, "5. After a plain await that started where checkpoint 4 left off"),
		new ExperimentCheckpoint(6, "6. Inside InvokeAsync(...)"),
	];

	public override IReadOnlyList<PredictionColumn> Columns { get; } =
	[
		new PredictionColumn(_contextColumn, "SynchronizationContext.Current",
		[
			new PredictionChoice(_rendererContext, "RendererSynchronizationContext (Blazor's renderer)"),
			new PredictionChoice(_noContext, "null (no SynchronizationContext)"),
			new PredictionChoice(_aspNetContext, "AspNetSynchronizationContext (the request's context)"),
		]),
	];

	public override IReadOnlyList<ExplainQuestion> Questions { get; } =
	[
		new ExplainQuestion("no-ui-thread", "Checkpoint 2 reports RendererSynchronizationContext, yet it can run on a different thread from checkpoint 1. What does that tell you about Blazor Server?",
		[
			new ExplainAnswer("a", "The await lost the context, so Blazor started a new UI thread for the component.", false,
				"If the context had been lost, checkpoint 2 would report null. Look at what it does report."),
			new ExplainAnswer("b", "Blazor Server has no UI thread. The renderer's context runs its work one item at a time on thread pool threads, so a continuation posted to it comes back to the context, not to a particular thread.", true,
				"Right. The context is what serializes access to component state. Which thread it happens to borrow does not matter."),
			new ExplainAnswer("c", "Every circuit has one UI thread, and its managed thread ID changes after each await.", false,
				"A thread's managed ID never changes. Different IDs mean different threads."),
		]),
		new ExplainQuestion("synchronous-completion", "Checkpoint 3 comes after ConfigureAwait(false), but still reports RendererSynchronizationContext. Why?",
		[
			new ExplainAnswer("a", "The cached story's ValueTask had already completed, so the await never scheduled a thread switch. The method kept running on the same thread, in the same context.", true,
				"Right. ConfigureAwait(false) only affects continuations that are actually scheduled, so it never guarantees that the code after it leaves the context."),
			new ExplainAnswer("b", "ConfigureAwait(false) only works on Task, not on ValueTask.", false,
				"Checkpoint 4 awaits a ValueTask from the same method, with ConfigureAwait(false), and does leave the context. What is different about the story it asks for?"),
			new ExplainAnswer("c", "Blazor ignores ConfigureAwait(false) inside event handlers.", false,
				"Checkpoint 4 is in the same event handler and does leave the context. What is different about the story it asks for?"),
		]),
		new ExplainQuestion("plain-await-does-not-come-back", "Checkpoint 5 comes after a plain await, but reports null. Why doesn't that await bring you back to the renderer?",
		[
			new ExplainAnswer("a", "The renderer was busy, so the await gave up waiting for it and used the thread pool instead.", false,
				"An await never gives up on a context it captured. Ask what SynchronizationContext.Current was when that await ran."),
			new ExplainAnswer("b", "ConfigureAwait(false) turns off context capture for every later await in the whole app.", false,
				"Checkpoint 6 is back on the renderer, and the next click starts there again. Ask what SynchronizationContext.Current was when checkpoint 5's await ran."),
			new ExplainAnswer("c", "A plain await captures whatever SynchronizationContext is current when that await runs. After checkpoint 4 there was none, so there was nothing to come back to. InvokeAsync(...) is how you get back.", true,
				"Right. That is why code after ConfigureAwait(false) marshals every component state change through InvokeAsync(...)."),
		]),
	];

	public override string GetHint(int checkpoint, string columnId) => checkpoint switch
	{
		1 => "Blazor runs every event handler through its renderer, and RefreshAsync() has not awaited anything yet. What is SynchronizationContext.Current while an event handler runs?",
		2 => "A plain await captures SynchronizationContext.Current and posts the continuation back to it. Compare the Thread column with checkpoint 1: the thread can change while the context stays the same.",
		3 => "The top story was already in the feed's cache, in [SimulatedStoryFeed.cs](Experiments/SimulatedStoryFeed.cs#A cached story comes back), so its ValueTask had completed before the await looked at it. ConfigureAwait(false) only changes where a continuation is scheduled. Was one scheduled?",
		4 => "This story had to wait for the simulated network, so the await scheduled a continuation, and ConfigureAwait(false) told it not to capture the context. What posts that continuation back to the renderer?",
		5 => "A plain await captures whatever SynchronizationContext is current when that await runs. What was current at checkpoint 4?",
		_ => "InvokeAsync(...) runs the lambda through Blazor's renderer. Compare the Thread column with checkpoint 5: the thread can be the same while the context is not.",
	};

	public override async Task<IReadOnlyList<CheckpointResult>> RunAsync(Func<Action, Task> invokeAsync, CancellationToken token)
	{
		var log = new CheckpointLog<string?>();
		var experiment = new SynchronizationContextExperiment(log);

		// No Task.Run and no ConfigureAwait(false) before this call: RefreshAsync() starts synchronously,
		// on Blazor's renderer, exactly like the click handler that called this method
		var refreshTask = experiment.RefreshAsync(invokeAsync, token);

		await refreshTask.WaitAsync(Timeout, token).ConfigureAwait(false);

		return [.. log.Observations.Select(static observation => new CheckpointResult(
			observation.Checkpoint,
			observation.ThreadId,
			observation.IsThreadPoolThread,
			new Dictionary<string, ObservedValue>
			{
				[_contextColumn] = new ObservedValue(Classify(observation.Value), observation.Value ?? "null"),
			},
			[]))];
	}

	static string Classify(string? synchronizationContextName) => synchronizationContextName switch
	{
		"RendererSynchronizationContext" => _rendererContext,
		null => _noContext,
		"AspNetSynchronizationContext" => _aspNetContext,
		_ => _otherContext,
	};
}