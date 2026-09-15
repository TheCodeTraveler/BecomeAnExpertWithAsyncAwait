namespace ImportPortal;

// Workshop plumbing: runs the steps against your services and keeps their results.
// Every step creates its own instances of the services, so a check never disturbs the singletons the Import page uses.
// Only one step runs at a time, so a step that measures CPU time or concurrent calls never shares the machine with another run.
public sealed class StepVerifier(ILogger<StepVerifier> logger, IHostApplicationLifetime applicationLifetime) : BackgroundService
{
	public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

	readonly Lock _lock = new();
	readonly StepState[] _states = [.. Enumerable.Repeat(new StepState(StepStatus.NotRun, null), 3)];

	// Both start at 1: the app checks every step on startup, and nothing else may run before that finishes
	int _isBusy = 1;
	int _isCheckingAllSteps = 1;

	ActiveStepRun? _activeRun;
	TaskCompletionSource? _stopRequested;

	public IReadOnlyList<WorkshopStep> Steps { get; } =
	[
		new Step1ScoreEveryRow(),
		new Step2EnrichEveryRow(),
		new Step3SummarizeAndCancel(),
	];

	public bool IsBusy => Volatile.Read(ref _isBusy) is 1;

	public bool IsCheckingAllSteps => Volatile.Read(ref _isCheckingAllSteps) is 1;

	public ActiveStepRun? ActiveRun
	{
		get
		{
			lock (_lock)
			{
				return _activeRun;
			}
		}
	}

	public WorkshopStep? FindStep(int number) => Steps.FirstOrDefault(step => step.Number == number);

	public StepState GetState(int number)
	{
		lock (_lock)
		{
			return _states[number - 1];
		}
	}

	public bool IsUnlocked(int number) => number is 1 || GetState(number - 1).Status is StepStatus.Passed;

	// Re-runs every step, the same way startup does
	public async Task CheckAllSteps()
	{
		if (Interlocked.CompareExchange(ref _isBusy, 1, 0) is not 0)
			return;

		Volatile.Write(ref _isCheckingAllSteps, 1);

		try
		{
			await CheckSteps(applicationLifetime.ApplicationStopping).ConfigureAwait(false);
		}
		finally
		{
			Volatile.Write(ref _isCheckingAllSteps, 0);
			Volatile.Write(ref _isBusy, 0);
		}
	}

	public async Task<StepState> RunStep(WorkshopStep step)
	{
		if (!IsUnlocked(step.Number) || Interlocked.CompareExchange(ref _isBusy, 1, 0) is not 0)
			return GetState(step.Number);

		try
		{
			return await Run(step, applicationLifetime.ApplicationStopping).ConfigureAwait(false);
		}
		finally
		{
			Volatile.Write(ref _isBusy, 0);
		}
	}

	public void StopRun()
	{
		lock (_lock)
		{
			_stopRequested?.TrySetResult();
		}
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			await CheckSteps(stoppingToken).ConfigureAwait(false);
		}
		finally
		{
			Volatile.Write(ref _isCheckingAllSteps, 0);
			Volatile.Write(ref _isBusy, 0);
		}
	}

	async Task CheckSteps(CancellationToken token)
	{
		try
		{
			// Results from before the latest change to the services are not trusted
			lock (_lock)
			{
				Array.Fill(_states, new StepState(StepStatus.NotRun, null));
			}

			logger.LogInformation("Checking your services against every step. Open the app in your browser: the workshop guide beside the Import page shows what to fix next");

			foreach (var step in Steps)
			{
				var state = await Run(step, token).ConfigureAwait(false);

				if (state.Status is not StepStatus.Passed)
				{
					logger.LogInformation("Stopped checking at Step {StepNumber}. Open it in the app to see what to change, with a hint for every result that did not match. Later steps unlock after it passes", step.Number);
					return;
				}
			}

			logger.LogInformation("Every step passed. Pause here for the group review");
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			// The app is shutting down
		}
		catch (Exception e)
		{
			logger.LogError(e, "Checking the steps failed unexpectedly");
		}
	}

	async Task<StepState> Run(WorkshopStep step, CancellationToken token)
	{
		var report = new StepReport();
		var stopRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var previousState = GetState(step.Number);

		lock (_lock)
		{
			_activeRun = new ActiveStepRun(step, report);
			_stopRequested = stopRequested;
			_states[step.Number - 1] = new StepState(StepStatus.Running, report);
		}

		// Cancelled when the run ends for any reason, so a step that is still working can stop
		using var runCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);

		var state = previousState;

		try
		{
			state = await Execute(step, report, stopRequested.Task, runCancellationTokenSource.Token, token).ConfigureAwait(false);
		}
		finally
		{
			await runCancellationTokenSource.CancelAsync().ConfigureAwait(false);

			report.Complete();

			lock (_lock)
			{
				_activeRun = null;
				_stopRequested = null;
				_states[step.Number - 1] = state with { CompletedAt = DateTimeOffset.Now };
			}
		}

		return state;
	}

	async Task<StepState> Execute(WorkshopStep step, StepReport report, Task stopRequested, CancellationToken runToken, CancellationToken token)
	{
		try
		{
			// A dedicated thread runs the step until its first await. If a deadlock blocks it forever,
			// a thread pool thread is not lost for the rest of the app's life.
			var stepTask = Task.Factory.StartNew(() => step.Run(new StepContext(report, runToken)), token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

			var finished = await Task.WhenAny(stepTask, stopRequested).WaitAsync(Timeout, token).ConfigureAwait(false);

			if (finished == stopRequested)
			{
				logger.LogInformation("Step {StepNumber} was stopped", step.Number);
				return new StepState(StepStatus.Stopped, report);
			}

			await stepTask.ConfigureAwait(false);

			var checks = report.Checks;
			var passedChecks = checks.Count(static check => check.Passed);
			var status = checks.Count > 0 && passedChecks == checks.Count ? StepStatus.Passed : StepStatus.Failed;

			logger.LogInformation("Step {StepNumber} {Result} ({PassedChecks}/{TotalChecks} checks)", step.Number, status is StepStatus.Passed ? "passed" : "failed", passedChecks, checks.Count);

			// The hints belong in the app's log output too, next to anything the app logged while the step ran
			foreach (var check in checks.Where(static check => !check.Passed))
			{
				logger.LogWarning("Step {StepNumber}: {Description}. Expected {Expected}, actual {Actual}. Hint: {Hint}", step.Number, check.Description, check.Expected, check.Actual, check.Hint);
			}

			return new StepState(status, report);
		}
		catch (TimeoutException)
		{
			logger.LogWarning("Step {StepNumber} did not finish within {Timeout}. {TimeoutHint}", step.Number, Timeout, step.TimeoutHint);
			return new StepState(StepStatus.TimedOut, report);
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			return new StepState(StepStatus.NotRun, null);
		}
		catch (Exception e)
		{
			logger.LogError(e, "Step {StepNumber} threw an unexpected exception", step.Number);
			return new StepState(StepStatus.Crashed, report, e.GetType().Name, e.ToString());
		}
	}
}