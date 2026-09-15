using System.Security.Claims;

namespace InternalsLab;

// This file holds the answers to Step 2. Run the experiment and answer the questions on the page before you read it.
public sealed class Step2ExecutionContext : WorkshopStep
{
	const string _valuesColumn = "values";
	const string _mainThreadValues = "main";
	const string _backgroundThreadValues = "background";
	const string _defaultValues = "defaults";
	const string _mixedValues = "mixed";

	public override int Number => 2;

	public override string Scenario => "ExecutionContext";

	public override string Title => "ExecutionContext.Capture(), ExecutionContext.Run(...), Task.Run(...), ExecutionContext.SuppressFlow()";

	public override string Story => "Step 1 showed that [ThreadStatic] values stay behind when async code moves to another thread. ExecutionContext is what .NET uses instead: "
		+ "it carries CultureInfo.CurrentCulture, Thread.CurrentPrincipal and every AsyncLocal<T> value, and await, Task.Run(...) and new threads capture it and restore it wherever the code runs next. "
		+ "The experiment is the console app from the slides. Its main thread assigns a culture, a principal and an AsyncLocal<string> value, a background thread assigns values of its own, "
		+ "and every checkpoint records what the code can see. Predict whose values each checkpoint sees.";

	public override int RecommendedMinutes => 12;

	public override string ExperimentFile => "Experiments/ExecutionContextExperiment.cs";

	public override IReadOnlyList<string> TryIt { get; } =
	[
		"Click Try it after you run the experiment. It runs AwaitInsideSuppressFlowAsync(), which awaits inside the using block instead of after it.",
		"Comment out the three assignments at the start of the background thread and apply the change with Hot Reload, then click Run it again. What does checkpoint 2 see now, and what does that tell you about new Thread(...)?",
	];

	public override IReadOnlyList<ExperimentCheckpoint> Checkpoints { get; } =
	[
		new ExperimentCheckpoint(1, "1. Main thread, after assigning its values"),
		new ExperimentCheckpoint(2, "2. Background thread, after assigning its own values"),
		new ExperimentCheckpoint(3, "3. Same background thread, inside ExecutionContext.Run(mainThreadExecutionContext, ...)"),
		new ExperimentCheckpoint(4, "4. Main thread, after the background thread finishes"),
		new ExperimentCheckpoint(5, "5. Inside Task.Run(...)"),
		new ExperimentCheckpoint(6, "6. Inside Task.Run(...) started while ExecutionContext flow is suppressed"),
	];

	public override IReadOnlyList<PredictionColumn> Columns { get; } =
	[
		new PredictionColumn(_valuesColumn, "CultureInfo.CurrentCulture, Thread.CurrentPrincipal, _asyncLocalData.Value",
		[
			new PredictionChoice(_mainThreadValues, "The main thread's values"),
			new PredictionChoice(_backgroundThreadValues, "The background thread's own values"),
			new PredictionChoice(_defaultValues, "Default values"),
		]),
	];

	public override IReadOnlyList<ExplainQuestion> Questions { get; } =
	[
		new ExplainQuestion("execution-context-run", "What changes when the background thread calls ExecutionContext.Run(mainThreadExecutionContext, ...)?",
		[
			new ExplainAnswer("a", "The callback is handed to the main thread, which runs it.", false,
				"Compare the Thread column for checkpoints 2 and 3."),
			new ExplainAnswer("b", "The main thread's values are copied over the background thread's values for the rest of that thread's life.", false,
				"The captured context only applies while the callback runs. What would the background thread see if it recorded its values again after ExecutionContext.Run(...) returns?"),
			new ExplainAnswer("c", "The callback runs on the same background thread, but with the main thread's captured ExecutionContext, so it sees the main thread's culture, principal and AsyncLocal value.", true,
				"Right. ExecutionContext.Run(...) applies the captured context for the duration of the callback, then puts the thread's own context back."),
		]),
		new ExplainQuestion("task-run-flow", "Checkpoints 5 and 6 both run inside Task.Run(...). Why does checkpoint 5 see the main thread's values while checkpoint 6 sees defaults?",
		[
			new ExplainAnswer("a", "Checkpoint 6 ran on a thread pool thread that had never seen the main thread's values.", false,
				"The thread does not decide it: the same thread pool thread could run either checkpoint. Look at what each task captured when it was created."),
			new ExplainAnswer("b", "Task.Run(...) captures the current ExecutionContext when it creates the task and restores it on the thread pool thread. Inside SuppressFlow() there is nothing to capture, so checkpoint 6 runs with the default, empty context.", true,
				"Right. ExecutionContext belongs to the work item, not to the thread that happens to run it."),
			new ExplainAnswer("c", "SuppressFlow() cleared the culture, principal and AsyncLocal value that the main thread assigned.", false,
				"SuppressFlow() stops ExecutionContext from being captured. It does not clear anything: the code after the using block still has the main thread's values."),
		]),
		new ExplainQuestion("await-after-the-using-block", "Why is the checkpoint 6 task created inside using (ExecutionContext.SuppressFlow()) but awaited only after the block ends?",
		[
			new ExplainAnswer("a", "Awaiting inside the block would deadlock the main thread.", false,
				"Nothing blocks a thread here. Click Try it to see what really happens when the await is inside the block."),
			new ExplainAnswer("b", "The task does not start running until the using block ends.", false,
				"Task.Run(...) queues the work right away. The using block only decides whether ExecutionContext is captured."),
			new ExplainAnswer("c", "SuppressFlow() returns a thread-affine AsyncFlowControl. An await inside the block lets the continuation end the block, often on another thread and with flow no longer suppressed, and disposing the AsyncFlowControl there throws InvalidOperationException.", true,
				"Right. Create the task inside the block, leave the block on the thread that entered it, and only then await."),
		]),
	];

	public override string GetHint(int checkpoint, string columnId) => checkpoint switch
	{
		1 => "Checkpoint 1 runs right after the main thread assigns CultureInfo.CurrentCulture, Thread.CurrentPrincipal and _asyncLocalData.Value. Look at the three lines above it.",
		2 => "The background thread assigned three values of its own just before checkpoint 2. Those assignments change the ExecutionContext of the thread that made them.",
		3 => "Look at the first argument to ExecutionContext.Run(...). The callback runs on the background thread (compare the Thread column with checkpoint 2), but with a context that was captured somewhere else.",
		4 => "The background thread assigned its own values and then finished. Did its assignments change the main thread's ExecutionContext, or only its own?",
		5 => "Task.Run(...) captured ExecutionContext when the task was created. Which thread created it, and which values did that thread have at the time?",
		_ => "This task was created inside using (ExecutionContext.SuppressFlow()). What can Task.Run(...) capture while flow is suppressed?",
	};

	public override async Task<IReadOnlyList<CheckpointResult>> RunAsync(Func<Action, Task> invokeAsync, CancellationToken token)
	{
		var log = new CheckpointLog<ExecutionContextValues>();
		var experiment = new ExecutionContextExperiment(log);

		await RunOnDedicatedThread(experiment.RunAsync, token).ConfigureAwait(false);

		return [.. log.Observations.Select(observation => new CheckpointResult(
			observation.Checkpoint,
			observation.ThreadId,
			observation.IsThreadPoolThread,
			new Dictionary<string, ObservedValue>
			{
				[_valuesColumn] = Classify(observation.Value),
			},
			[
				$"Culture: {observation.Value.Culture}",
				$"Principal: {observation.Value.PrincipalType ?? "null"}",
				$"AsyncLocal: {observation.Value.AsyncLocalData ?? "null"}",
			]))];
	}

	// Runs AwaitInsideSuppressFlowAsync() on its own dedicated thread, the same way RunAsync() starts the experiment, and never lets its exception escape
	public async Task<TryItOutcome> TryAwaitingInsideTheUsingBlock(ILogger logger, CancellationToken token)
	{
		var log = new CheckpointLog<ExecutionContextValues>();
		var experiment = new ExecutionContextExperiment(log);
		string? exceptionType = null;

		try
		{
			await RunOnDedicatedThread(experiment.AwaitInsideSuppressFlowAsync, token).ConfigureAwait(false);
		}
		catch (InvalidOperationException e)
		{
			// Expected: the full exception belongs in the terminal, not in the browser
			logger.LogInformation(e, "Try it: awaiting inside using (ExecutionContext.SuppressFlow()) threw, as expected");
			exceptionType = e.GetType().Name;
		}

		var observations = log.Observations;

		return new TryItOutcome(
			exceptionType,
			observations.FirstOrDefault(static observation => observation.Checkpoint is 1)?.ThreadId,
			observations.FirstOrDefault(static observation => observation.Checkpoint is 3)?.ThreadId,
			DateTimeOffset.Now);
	}

	ObservedValue Classify(ExecutionContextValues values)
	{
		// The values ExecutionContextExperiment assigns on its main thread and on its background thread
		var choiceId = values switch
		{
			{ Culture: "es-ES", PrincipalType: nameof(ClaimsPrincipal), AsyncLocalData: "Initial Value" } => _mainThreadValues,
			{ Culture: "en-GB", PrincipalType: nameof(CustomPrincipal), AsyncLocalData: "AsyncLocalData in Thread" } => _backgroundThreadValues,
			{ PrincipalType: null, AsyncLocalData: null } => _defaultValues,
			_ => _mixedValues,
		};

		var label = Columns[0].Choices.FirstOrDefault(choice => choice.Id == choiceId)?.Label ?? "A mix of values";

		return new ObservedValue(choiceId, label);
	}
}