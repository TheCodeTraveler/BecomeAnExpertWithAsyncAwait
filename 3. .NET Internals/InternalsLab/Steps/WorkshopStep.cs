using System.Diagnostics;

namespace InternalsLab;

// Every step is one experiment about where .NET keeps the state that async code depends on. Nothing is broken, so there is nothing to fix.
// Its page asks you to predict every checkpoint, runs the experiment, shows what really happened next to your predictions,
// and asks you to explain what you saw. The step files hold the answers, so run the experiment before you read them.
public abstract class WorkshopStep
{
	// Long enough for any experiment, and short enough that a Try it change that blocks forever cannot hang the page.
	// With a debugger attached there is no limit, so pausing at a breakpoint never turns into a timeout.
	public static TimeSpan Timeout => Debugger.IsAttached ? System.Threading.Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(10);

	public abstract int Number { get; }

	// The piece of ambient state this step investigates, in a few words
	public abstract string Scenario { get; }

	// The APIs the experiment exercises
	public abstract string Title { get; }

	// Why this state matters, and what the experiment does
	public abstract string Story { get; }

	public abstract int RecommendedMinutes { get; }

	// The code to read, and where the step's // Step N: comments are
	public abstract string ExperimentFile { get; }

	// The same suggestions as the // Try it: comments in the experiment code
	public abstract IReadOnlyList<string> TryIt { get; }

	public abstract IReadOnlyList<ExperimentCheckpoint> Checkpoints { get; }

	public abstract IReadOnlyList<PredictionColumn> Columns { get; }

	public abstract IReadOnlyList<ExplainQuestion> Questions { get; }

	// Steps whose experiment is an MVC request return its URL, and the page loads it instead of calling RunAsync()
	public virtual string? ExperimentUrl => null;

	public static string PredictionKey(int checkpoint, string columnId) => $"{checkpoint}:{columnId}";

	public int CountPredictions(IReadOnlyDictionary<string, string> predictions) =>
		Checkpoints.Sum(checkpoint => Columns.Count(column => predictions.ContainsKey(PredictionKey(checkpoint.Number, column.Id))));

	public bool HasAllPredictions(IReadOnlyDictionary<string, string> predictions) => CountPredictions(predictions) == Checkpoints.Count * Columns.Count;

	public bool IsAnsweredCorrectly(ExplainQuestion question, IReadOnlyDictionary<string, string> answers) =>
		question.FindAnswer(answers.GetValueOrDefault(question.Id))?.IsCorrect is true;

	// What to look at when a result does not match your prediction. It points at the code, not at the answer.
	public abstract string GetHint(int checkpoint, string columnId);

	// Runs the experiment inside this Blazor circuit and returns what every checkpoint saw.
	// The page calls it straight from its click handler, so it starts on Blazor's renderer.
	public abstract Task<IReadOnlyList<CheckpointResult>> RunAsync(Func<Action, Task> invokeAsync, CancellationToken token);

	// Starts an experiment on a new dedicated thread, which plays the part of a console app's main thread.
	// Flow is suppressed while the thread starts, so none of this Blazor circuit's ExecutionContext leaks into the experiment,
	// and nothing the experiment assigns can leak back out. The task is awaited after the using block, on the thread that created it.
	protected static async Task RunOnDedicatedThread(Func<Task> experiment, CancellationToken token)
	{
		Task experimentTask;
		using (ExecutionContext.SuppressFlow())
		{
			experimentTask = Task.Factory.StartNew(experiment, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
		}

		await experimentTask.WaitAsync(Timeout, token).ConfigureAwait(false);
	}
}