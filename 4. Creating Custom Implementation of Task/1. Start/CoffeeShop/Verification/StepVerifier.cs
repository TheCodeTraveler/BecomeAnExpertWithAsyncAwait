namespace CoffeeShop;

// Workshop plumbing: runs the steps against your CustomTask and keeps their results.
// Everything in this file uses the real Task, so a broken CustomTask cannot break the verifier itself.
// Only one step runs at a time, so timing checks never compete with another run for thread pool threads.
public sealed class StepVerifier(ILogger<StepVerifier> logger, IHostApplicationLifetime applicationLifetime) : BackgroundService
{
	public static readonly TimeSpan AutomaticTimeout = TimeSpan.FromSeconds(10);
	public static readonly TimeSpan ManualTimeout = TimeSpan.FromMinutes(2);

	static readonly TimeSpan _automaticBaristaDelay = TimeSpan.FromMilliseconds(300);
	static readonly TimeSpan _automaticBaristaPollingInterval = TimeSpan.FromMilliseconds(50);

	readonly Lock _lock = new();
	readonly StepState[] _states = [.. Enumerable.Repeat(new StepState(StepStatus.NotRun, null), 6)];

	// Both start at 1: the app checks every step on startup, and nothing else may run before that finishes
	int _isBusy = 1;
	int _isCheckingAllSteps = 1;

	ActiveStepRun? _activeRun;
	TaskCompletionSource<Exception>? _baristaFault;
	TaskCompletionSource? _stopRequested;

	public IReadOnlyList<WorkshopStep> Steps { get; } =
	[
		new Step1PrintReceipt(),
		new Step2BrewPourOver(),
		new Step3MobileOrder(),
		new Step4PullShot(),
		new Step5HandleJam(),
		new Step6RushHour(),
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

	// Re-runs every step that is checked on startup, the same way startup does
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

	public async Task<StepState> RunStep(WorkshopStep step, bool isBaristaAutomatic)
	{
		if (!IsUnlocked(step.Number) || Interlocked.CompareExchange(ref _isBusy, 1, 0) is not 0)
			return GetState(step.Number);

		try
		{
			// A human pressing the barista's buttons gets more time than the automatic barista
			var timeout = step.BaristaAction is BaristaAction.None || isBaristaAutomatic ? AutomaticTimeout : ManualTimeout;

			return await Run(step, isBaristaAutomatic, timeout, applicationLifetime.ApplicationStopping).ConfigureAwait(false);
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

	public async Task PressBaristaButton(BaristaAction action)
	{
		ActiveStepRun? run;
		TaskCompletionSource<Exception>? baristaFault;

		lock (_lock)
		{
			run = _activeRun;
			baristaFault = _baristaFault;
		}

		if (run is not null && baristaFault is not null)
			await PressBaristaButton(run.EspressoMachine, action, baristaFault, CancellationToken.None).ConfigureAwait(false);
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

	// Run(), Delay() and ContinueWith() call SetResult() and SetException() from thread pool and timer callbacks,
	// where an exception cannot be caught and ends the whole app. Calling both here first, on the verifier's own thread
	// and on CustomTasks that have no continuations, reports a member that still throws instead of crashing.
	static void ProbeCompletionMembers()
	{
		new CustomTask().SetResult();
		new CustomTask().SetException(new InvalidOperationException("Checking that SetException() is implemented"));
	}

	// Finds the CustomTask member named by a stub's NotImplementedException.
	// The stub message is checked first because it survives a rethrow that resets the stack trace.
	static string? FindMissingMember(Exception exception)
	{
		const string stubMessageSuffix = " is not implemented yet";

		for (var current = exception; current is not null; current = current.InnerException)
		{
			if (current is not NotImplementedException)
				continue;

			var suffixIndex = current.Message.IndexOf(stubMessageSuffix, StringComparison.Ordinal);

			if (suffixIndex > 0 && current.Message.StartsWith(nameof(CustomTask), StringComparison.Ordinal))
				return current.Message[..suffixIndex];

			if (current.TargetSite is { } method && method.DeclaringType == typeof(CustomTask))
				return method.IsSpecialName ? $"{nameof(CustomTask)}.{method.Name.Replace("get_", string.Empty, StringComparison.Ordinal)}" : $"{nameof(CustomTask)}.{method.Name}()";
		}

		return null;
	}

	async Task CheckSteps(CancellationToken token)
	{
		try
		{
			// Results from before the latest change to CustomTask.cs are not trusted
			lock (_lock)
			{
				Array.Fill(_states, new StepState(StepStatus.NotRun, null));
			}

			logger.LogInformation("Checking your CustomTask against every step that runs on startup");

			foreach (var step in Steps.Where(static step => step.VerifyOnStartup))
			{
				var state = await Run(step, true, AutomaticTimeout, token).ConfigureAwait(false);

				if (state.Status is not StepStatus.Passed)
				{
					logger.LogInformation("Stopped checking at Step {StepNumber}. Later steps unlock after it passes", step.Number);
					return;
				}
			}

			logger.LogInformation("Every step that runs on startup passed");
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

	async Task<StepState> Run(WorkshopStep step, bool isBaristaAutomatic, TimeSpan timeout, CancellationToken token)
	{
		var report = new StepReport();
		var machine = new EspressoMachine();
		var baristaFault = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
		var stopRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var previousState = GetState(step.Number);

		lock (_lock)
		{
			_activeRun = new ActiveStepRun(step, report, machine, isBaristaAutomatic);
			_baristaFault = baristaFault;
			_stopRequested = stopRequested;
			_states[step.Number - 1] = new StepState(StepStatus.Running, report);
		}

		using var baristaCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);

		var barista = isBaristaAutomatic && step.BaristaAction is not BaristaAction.None
			? RunAutomaticBarista(machine, step.BaristaAction, baristaFault, baristaCancellationTokenSource.Token)
			: Task.CompletedTask;

		var state = previousState;

		try
		{
			state = await Execute(step, report, machine, baristaFault.Task, stopRequested.Task, timeout, token).ConfigureAwait(false);
		}
		finally
		{
			await baristaCancellationTokenSource.CancelAsync().ConfigureAwait(false);

			try
			{
				await barista.ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (baristaCancellationTokenSource.Token.IsCancellationRequested)
			{
				// The step finished, so the automatic barista is no longer needed
			}

			report.Complete();

			lock (_lock)
			{
				_activeRun = null;
				_baristaFault = null;
				_stopRequested = null;
				_states[step.Number - 1] = state with { CompletedAt = DateTimeOffset.Now };
			}
		}

		return state;
	}

	async Task<StepState> Execute(WorkshopStep step, StepReport report, EspressoMachine machine, Task<Exception> baristaFault, Task stopRequested, TimeSpan timeout, CancellationToken token)
	{
		try
		{
			ProbeCompletionMembers();

			// A dedicated thread runs the step until its first await. If a broken Wait() blocks it forever,
			// a thread pool thread is not lost for the rest of the app's life.
			var stepTask = Task.Factory.StartNew(() => step.Run(new StepContext(report, machine)), token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

			var finished = await Task.WhenAny(stepTask, baristaFault, stopRequested).WaitAsync(timeout, token).ConfigureAwait(false);

			if (finished == stopRequested)
			{
				logger.LogInformation("Step {StepNumber} was stopped", step.Number);
				return new StepState(StepStatus.Stopped, report);
			}

			// The event handler that ran SetResult() or SetException() threw on the barista's thread
			if (finished == baristaFault)
				return Classify(step, report, await baristaFault.ConfigureAwait(false));

			await stepTask.ConfigureAwait(false);

			var checks = report.Checks;
			var passedChecks = checks.Count(static check => check.Passed);
			var status = checks.Count > 0 && passedChecks == checks.Count ? StepStatus.Passed : StepStatus.Failed;

			logger.LogInformation("Step {StepNumber} {Result} ({PassedChecks}/{TotalChecks} checks)", step.Number, status is StepStatus.Passed ? "passed" : "failed", passedChecks, checks.Count);

			return new StepState(status, report);
		}
		catch (TimeoutException)
		{
			logger.LogWarning("Step {StepNumber} did not finish within {Timeout}. {TimeoutHint}", step.Number, timeout, step.TimeoutHint);
			return new StepState(StepStatus.TimedOut, report, WasWaitingForBarista: machine.IsBrewing);
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			return new StepState(StepStatus.NotRun, null);
		}
		catch (Exception e)
		{
			return Classify(step, report, e);
		}
	}

	StepState Classify(WorkshopStep step, StepReport report, Exception exception)
	{
		if (FindMissingMember(exception) is { } missingMember)
		{
			// The stub's message is the hint, so it belongs in the app's log output too
			logger.LogWarning(exception, "Step {StepNumber}: {MissingMember} still throws NotImplementedException", step.Number, missingMember);
			return new StepState(StepStatus.NotImplemented, report, missingMember);
		}

		logger.LogError(exception, "Step {StepNumber} threw an unexpected exception", step.Number);
		return new StepState(StepStatus.Crashed, report, ExceptionType: exception.GetType().Name, Exception: exception.ToString());
	}

	// Stands in for a barista at startup: waits until a shot is brewing, then presses the button a moment later
	async Task RunAutomaticBarista(EspressoMachine machine, BaristaAction action, TaskCompletionSource<Exception> baristaFault, CancellationToken token)
	{
		while (true)
		{
			while (!machine.IsBrewing)
			{
				await Task.Delay(_automaticBaristaPollingInterval, token).ConfigureAwait(false);
			}

			await Task.Delay(_automaticBaristaDelay, token).ConfigureAwait(false);

			await PressBaristaButton(machine, action, baristaFault, token).ConfigureAwait(false);
		}
	}

	// FinishShot() and Jam() raise the machine's events, and the step's handlers call SetResult() or SetException().
	// Task.Run keeps that off Blazor's renderer thread, and a handler that throws ends the run instead of hanging it.
	async Task PressBaristaButton(EspressoMachine machine, BaristaAction action, TaskCompletionSource<Exception> baristaFault, CancellationToken token)
	{
		try
		{
			await Task.Run(
				() =>
				{
					if (action is BaristaAction.JamMachine)
						machine.Jam();
					else
						machine.FinishShot();
				},
				token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception e)
		{
			// The run classifies and logs the exception. Log it here only when that run has already ended.
			if (!baristaFault.TrySetResult(e))
				logger.LogError(e, "The espresso machine's event handler threw after the barista pressed {BaristaAction}", action);
		}
	}
}