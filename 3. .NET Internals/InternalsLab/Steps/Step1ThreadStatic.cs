namespace InternalsLab;

// This file holds the answers to Step 1. Run the experiment and answer the questions on the page before you read it.
public sealed class Step1ThreadStatic : WorkshopStep
{
	const string _valueColumn = "value";
	const string _mainThreadValue = "main";
	const string _defaultValue = "default";
	const string _ownValue = "own";

	public override int Number => 1;

	public override string Scenario => "ThreadStatic";

	public override string Title => "[ThreadStatic] static int _threadSpecificValue";

	public override string Story => "Before async and await, ambient data such as the current user or an open database transaction usually lived in thread-local storage, and [ThreadStatic] is the simplest way to get it: "
		+ "a static field with a separate copy for every thread. The experiment is a small console-style program. Its main thread assigns 100, two background threads each read the field and then assign a random value of their own, "
		+ "and finally the main thread awaits Task.Yield(), which moves the rest of the method to a thread pool thread. Predict what the field holds at every checkpoint.";

	public override int RecommendedMinutes => 8;

	public override string ExperimentFile => "Experiments/ThreadStaticExperiment.cs";

	public override IReadOnlyList<string> TryIt { get; } =
	[
		"Remove [ThreadStatic], then stop and run the app again. Restarting starts a fresh notebook, so try this one after the group review. Which checkpoints change, and which threads share the value now?",
		"Replace the field with static readonly AsyncLocal<int> and read and write its Value, then run the app again. Which checkpoints change? Step 2 explains why.",
	];

	public override IReadOnlyList<ExperimentCheckpoint> Checkpoints { get; } =
	[
		new ExperimentCheckpoint(1, "1. Main thread, after assigning 100"),
		new ExperimentCheckpoint(2, "2. Background thread 1, before assigning"),
		new ExperimentCheckpoint(3, "3. Background thread 1, after assigning its random value"),
		new ExperimentCheckpoint(4, "4. Background thread 2, before assigning"),
		new ExperimentCheckpoint(5, "5. Background thread 2, after assigning its random value"),
		new ExperimentCheckpoint(6, "6. Main thread, after both threads finish"),
		new ExperimentCheckpoint(7, "7. After await Task.Yield()"),
	];

	public override IReadOnlyList<PredictionColumn> Columns { get; } =
	[
		new PredictionColumn(_valueColumn, "_threadSpecificValue",
		[
			new PredictionChoice(_mainThreadValue, "100 (the main thread's value)"),
			new PredictionChoice(_defaultValue, "0 (the default for int)"),
			new PredictionChoice(_ownValue, "Its own random value"),
		]),
	];

	public override IReadOnlyList<ExplainQuestion> Questions { get; } =
	[
		new ExplainQuestion("main-thread-keeps-its-value", "Why does the main thread still read 100 at checkpoint 6, after both background threads assigned their own values?",
		[
			new ExplainAnswer("a", "Join() restores the main thread's value when the background threads finish.", false,
				"Join() only waits for a thread to finish. Nothing is saved or restored, so ask which copy of the field the background threads wrote to."),
			new ExplainAnswer("b", "The background threads ran after checkpoint 6, so their writes had not happened yet.", false,
				"Join() waits for both threads before checkpoint 6 runs, so their writes had already happened. Look at the checkpoint order."),
			new ExplainAnswer("c", "[ThreadStatic] gives every thread its own storage for the field, so the background threads wrote to their own copies and never touched the main thread's.", true,
				"Right. A [ThreadStatic] field is really one field per thread, and each thread only ever reads and writes its own copy."),
			new ExplainAnswer("d", "Every new thread gets a copy of the static fields when it starts, so the background threads changed their copies.", false,
				"If a new thread copied the value when it started, checkpoints 2 and 4 would read 100. What did they read?"),
		]),
		new ExplainQuestion("background-threads-start-at-zero", "Why do the background threads read 0 at checkpoints 2 and 4?",
		[
			new ExplainAnswer("a", "Each thread's copy of a [ThreadStatic] field starts at the default for its type, and nothing copies another thread's value into it.", true,
				"Right. A thread gets a fresh copy of the field, and for an int that copy starts at 0."),
			new ExplainAnswer("b", "The main thread had not assigned 100 yet when they started.", false,
				"The main thread assigned 100 at checkpoint 1, before it created either thread."),
			new ExplainAnswer("c", "Thread.Start() resets every static field to its default value.", false,
				"If it did, the main thread would read 0 at checkpoint 6 too."),
		]),
		new ExplainQuestion("await-leaves-the-value-behind", "Why does checkpoint 7, after await Task.Yield(), read 0?",
		[
			new ExplainAnswer("a", "await resets [ThreadStatic] fields to their default values.", false,
				"await never touches the field. Compare the Thread column for checkpoints 6 and 7."),
			new ExplainAnswer("b", "The continuation ran on a thread pool thread, which has its own copy of the field. [ThreadStatic] values stay with the thread, so they do not follow async code when it moves to another thread.", true,
				"Right. That is why async code needs storage that follows the flow of the code instead of the thread. Step 2 shows it: ExecutionContext and AsyncLocal<T>."),
			new ExplainAnswer("c", "The continuation ran on the same thread, but Task.Yield() cleared the thread's static fields.", false,
				"Compare the Thread column for checkpoints 6 and 7. Are they the same thread?"),
		]),
	];

	public override string GetHint(int checkpoint, string columnId) => checkpoint switch
	{
		1 => "Checkpoint 1 reads the field on the same thread that just assigned it. Look at the line right above log.Record(1, ...).",
		2 or 4 => "This thread has not assigned anything yet. The main thread assigned 100 before starting it, but whose copy of the field did that assignment change? What does a thread's own copy start as?",
		3 or 5 => "This thread just assigned Random.Shared.Next(1, 100) to the field. Compare its Thread column with checkpoint 1: whose copy is it reading?",
		6 => "Both background threads have written to _threadSpecificValue by now. Checkpoint 6 runs on the same thread as checkpoint 1, so ask which copy the background threads wrote to.",
		_ => "Compare the Thread and Pool thread columns for checkpoints 6 and 7. The code after await Task.Yield() ran on a different thread, and [ThreadStatic] storage belongs to a thread, not to a method.",
	};

	public override async Task<IReadOnlyList<CheckpointResult>> RunAsync(Func<Action, Task> invokeAsync, CancellationToken token)
	{
		var log = new CheckpointLog<int>();
		var experiment = new ThreadStaticExperiment(log);

		await RunOnDedicatedThread(experiment.RunAsync, token).ConfigureAwait(false);

		return [.. log.Observations.Select(static observation => new CheckpointResult(
			observation.Checkpoint,
			observation.ThreadId,
			observation.IsThreadPoolThread,
			new Dictionary<string, ObservedValue>
			{
				[_valueColumn] = new ObservedValue(Classify(observation.Value), observation.Value.ToString()),
			},
			[]))];
	}

	// Random.Shared.Next(1, 100) never returns 0 or 100, so any other value is a thread's own random value
	static string Classify(int value) => value switch
	{
		100 => _mainThreadValue,
		0 => _defaultValue,
		_ => _ownValue,
	};
}