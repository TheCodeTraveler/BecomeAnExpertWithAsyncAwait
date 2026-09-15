namespace InternalsLab;

// Workshop plumbing: your lab notebook. It keeps every step's predictions, latest results and answers for as long as the app runs.
// It is a singleton, so reloading a page, or leaving the Blazor app for the Step 3 experiment, never loses anything.
// Blazor circuits and MVC requests use it from different threads, so every public member takes the lock.
public sealed class LabNotebook
{
	readonly Lock _lock = new();
	readonly StepProgress[] _progress;

	public LabNotebook()
	{
		_progress = [.. Steps.Select(static _ => StepProgress.Empty)];
	}

	public IReadOnlyList<WorkshopStep> Steps { get; } =
	[
		new Step1ThreadStatic(),
		new Step2ExecutionContext(),
		new Step3Principal(),
		new Step4SynchronizationContext(),
	];

	public WorkshopStep? FindStep(int number) => Steps.FirstOrDefault(step => step.Number == number);

	public StepProgress GetProgress(int number)
	{
		lock (_lock)
		{
			return _progress[number - 1];
		}
	}

	public StepStatus GetStatus(int number)
	{
		lock (_lock)
		{
			return StatusOf(number);
		}
	}

	public bool IsUnlocked(int number)
	{
		lock (_lock)
		{
			return IsUnlockedWhileLocked(number);
		}
	}

	// Predictions lock in once the experiment has run, so the results always compare against what you predicted first
	public void Predict(int number, int checkpoint, string columnId, string choiceId)
	{
		var step = Steps[number - 1];
		var column = step.Columns.FirstOrDefault(column => column.Id == columnId);

		if (column is null || step.Checkpoints.All(existing => existing.Number != checkpoint))
			return;

		lock (_lock)
		{
			var progress = _progress[number - 1];

			if (!IsUnlockedWhileLocked(number) || progress.LatestRun is not null)
				return;

			var predictions = new Dictionary<string, string>(progress.Predictions);
			var key = WorkshopStep.PredictionKey(checkpoint, columnId);

			if (column.Choices.Any(choice => choice.Id == choiceId))
				predictions[key] = choiceId;
			else
				predictions.Remove(key);

			_progress[number - 1] = progress with { Predictions = predictions };
		}
	}

	// Ignored until the step is unlocked and every prediction is chosen, even when the experiment was started some other way
	public bool RecordRun(int number, IReadOnlyList<CheckpointResult> results)
	{
		var step = Steps[number - 1];

		lock (_lock)
		{
			var progress = _progress[number - 1];

			if (!IsUnlockedWhileLocked(number) || !step.HasAllPredictions(progress.Predictions))
				return false;

			var runNumber = (progress.LatestRun?.RunNumber ?? 0) + 1;
			var run = new ExperimentRun(runNumber, results, progress.Predictions, DateTimeOffset.Now);

			_progress[number - 1] = progress with { LatestRun = run, PreviousRun = progress.LatestRun };

			return true;
		}
	}

	public bool RecordPrincipalRun(IReadOnlyList<Checkpoint> checkpoints) => RecordRun(Step3Principal.StepNumber, Step3Principal.ToResults(checkpoints));

	// A correct answer stays correct, so answering a question can never lock a later step again
	public void Answer(int number, string questionId, string answerId)
	{
		var step = Steps[number - 1];
		var question = step.Questions.FirstOrDefault(question => question.Id == questionId);

		if (question?.FindAnswer(answerId) is null)
			return;

		lock (_lock)
		{
			var progress = _progress[number - 1];

			if (!IsUnlockedWhileLocked(number) || progress.LatestRun is null || step.IsAnsweredCorrectly(question, progress.Answers))
				return;

			var answers = new Dictionary<string, string>(progress.Answers)
			{
				[questionId] = answerId,
			};

			_progress[number - 1] = progress with { Answers = answers };
		}
	}

	public void RecordTryIt(int number, TryItOutcome outcome)
	{
		lock (_lock)
		{
			_progress[number - 1] = _progress[number - 1] with { TryItOutcome = outcome };
		}
	}

	// Callers already hold _lock
	StepStatus StatusOf(int number)
	{
		var step = Steps[number - 1];
		var progress = _progress[number - 1];

		if (progress.LatestRun is null)
			return step.HasAllPredictions(progress.Predictions) ? StepStatus.Predicted : StepStatus.NotStarted;

		return step.Questions.All(question => step.IsAnsweredCorrectly(question, progress.Answers)) ? StepStatus.Passed : StepStatus.Ran;
	}

	// Callers already hold _lock
	bool IsUnlockedWhileLocked(int number) => number is 1 || StatusOf(number - 1) is StepStatus.Passed;
}