namespace StockWatch;

public sealed class Step4GuardRefreshTimer : WorkshopStep
{
	// Starts and stops at the same time, the way pages that open, reload and close all day produce them
	const int _concurrentCalls = 400;

	// What a timer method did while this step held the semaphore
	const string _waited = "waited for the semaphore, then finished";
	const string _didNotWait = "finished without waiting for the semaphore";
	const string _blocked = "blocked a thread while it waited";
	const string _stillWaiting = "still waiting 1 second after the semaphore was released";

	const string _noSemaphoreHint = "Nothing orders StartRefreshTimer() against StopRefreshTimer(), so two callers can both read _refreshTimer and leak a live timer. "
		+ "Add a SemaphoreSlim created with new(1, 1), and guard every read and write of _refreshTimer with it. Do not use lock: it cannot be held across an await, and every method here awaits.";

	const string _skippedHint = "Add the semaphore first. Calling the unguarded timer methods at the same time leaks live timers, and a leaked timer that fires after DisposeAsync() throws inside an async void timer callback, which ends the whole app. "
		+ "So this check waits until the timer is guarded.";

	const string _releaseHint = "Something took the semaphore and never gave it back, so the next caller would wait forever. Release it inside a finally block, so every WaitAsync is matched by exactly one Release, even when the guarded code throws.";

	const string _deadlockHint = "SemaphoreSlim is not reentrant. A method that holds the semaphore and then calls another method that waits on it waits on itself forever. "
		+ "Move the timer disposal into a private method that assumes the semaphore is already held, and call that method from inside both guarded blocks.";

	const string _leakHint = "A timer is still refreshing a page that stopped its timer. Two calls read and wrote _refreshTimer at the same time, and one timer was replaced without being disposed. "
		+ "Hold the semaphore for the whole time a method reads, disposes, creates or assigns the timer, not just part of it.";

	// Longer than the timer's 2 second period, so any timer that is still running ticks at least once
	static readonly TimeSpan _tickWait = TimeSpan.FromSeconds(2.5);

	// How long this step holds the semaphore to see whether a timer method waits for it
	static readonly TimeSpan _holdTime = TimeSpan.FromMilliseconds(300);

	static readonly TimeSpan _callTimeout = TimeSpan.FromSeconds(1);

	public override int Number => 4;

	public override string Scenario => "Guard the refresh timer";

	public override string Title => "StartRefreshTimer(), StopRefreshTimer() and DisposeAsync()";

	public override string Story => "People open the dashboard, reload it, and close it all day, so the page starts and stops its refresh timer constantly. "
		+ "StartRefreshTimer() and StopRefreshTimer() both read and write _refreshTimer, and nothing orders them. A page torn down while it is still initializing can null the field out from under the method that is assigning it, "
		+ "and the timer it leaks keeps refreshing a page nobody is looking at. When that timer next fires, the CancellationTokenSource its callback reads has already been disposed, and an exception in an async void timer callback ends the whole process.";

	public override string SeeItInTheApp => "You will rarely see this one in the browser, which is exactly what makes it dangerous. It needs a page torn down at the same moment it starts its timer, such as a tab closed or reloaded while the dashboard is still loading. "
		+ "The Feed fault panel cannot catch this one: the exception is thrown on a timer thread, not while the page renders, so the whole app exits and the browser just loses its connection. Your IDE's Run or Debug output window (or the terminal, if you started the app with dotnet run) is the only place that tells you why.";

	public override string FileToChange => "Components/Pages/Dashboard.razor.cs";

	public override IReadOnlyList<string> Tasks { get; } =
	[
		"Add a SemaphoreSlim created with new(1, 1), and use it to guard every read and write of _refreshTimer.",
		"Do not use lock. lock cannot be held across an await, and every method here awaits.",
		"Wait on the semaphore with await WaitAsync(...), and release it inside a finally block.",
		"Move the timer disposal into a private method that assumes the semaphore is already held, and call that method from inside both guarded blocks.",
		"Dispose the semaphore in DisposeAsync().",
		"Keep the InvokeAsync(StateHasChanged) call and the CancellationToken plumbing that are already there.",
	];

	public override IReadOnlyList<string> Clues { get; } =
	[
		"StartRefreshTimer() and StopRefreshTimer() both touch _refreshTimer, and nothing stops them running at the same time.",
		"The guarded code awaits, so it needs a lock that can be awaited. A SemaphoreSlim with one slot is exactly that.",
		"SemaphoreSlim is not reentrant. StartRefreshTimer() calls StopRefreshTimer(), so if both take the semaphore, the first one waits on itself forever.",
		"A common pattern: public methods that take the semaphore, and one private method that assumes it is already held.",
		"StopRefreshTimer() runs from DisposeAsync() after the dispose token has been cancelled. If it waits with that token, cleanup never runs. Dispose the semaphore last, once nothing can wait on it again.",
	];

	public override string TimeoutHint => "A timer method never returned. Check that every WaitAsync is matched by a Release in a finally block, and that no method waits on the semaphore while it already holds it.";

	public override async Task Run(StepContext context)
	{
		var report = context.Report;
		var token = context.Token;

		if (!DashboardHarness.HasMembers(report, "_refreshTimer", "StartRefreshTimer", "StopRefreshTimer"))
			return;

		// Part 1: without a semaphore, calling the timer methods at the same time leaks live timers. A leaked timer that fires after DisposeAsync()
		// throws inside an async void callback, and that would end this app, so nothing below runs them at the same time until the timer is guarded.
		var semaphoreFields = DashboardHarness.FindSemaphoreFields();
		var semaphoreNames = string.Join(", ", semaphoreFields.Select(static field => field.Name));

		report.Log(semaphoreFields.Count is 0 ? "Dashboard.razor.cs has no SemaphoreSlim field" : $"Found the SemaphoreSlim field {semaphoreNames}");
		report.Expect("Dashboard.razor.cs has a SemaphoreSlim field to guard _refreshTimer", "a SemaphoreSlim field", semaphoreFields.Count is 0 ? "none" : semaphoreNames, semaphoreFields.Count > 0, _noSemaphoreHint);

		if (semaphoreFields.Count is 0)
		{
			const string skipped = "Skipped: nothing guards the timer yet";

			report.Expect($"{_concurrentCalls} StartRefreshTimer() and StopRefreshTimer() calls at the same time all finish", $"{_concurrentCalls} of {_concurrentCalls}", skipped, false, _skippedHint);
			report.Expect("No refresh runs after StopRefreshTimer()", "0 quotes applied", skipped, false, _skippedHint);
			report.Expect("DisposeAsync() finishes and disposes the semaphore", "finished, semaphore disposed", skipped, false, _skippedHint);
			return;
		}

		// Part 2: a fresh dashboard. OnInitializedAsync() starts the timer, so a method that waits on itself never lets the page finish loading.
		await using var harness = await DashboardHarness.Render().ConfigureAwait(false);

		if (!await harness.Load(report, token).ConfigureAwait(false))
			return;

		var semaphores = harness.GetSemaphores();
		var freeSemaphores = semaphores.Count(static semaphore => semaphore.CurrentCount > 0);

		report.Expect("Once the page has loaded, the semaphore is free again", "free", freeSemaphores == semaphores.Count ? "free" : "still held", freeSemaphores == semaphores.Count, _releaseHint);

		if (freeSemaphores != semaphores.Count)
		{
			harness.LeaveRunning();
			return;
		}

		// Part 3: with the timer already running, StartRefreshTimer() has to stop it first. That is where a guarded method calling another guarded method waits on itself.
		var restartOutcome = await DashboardHarness.Describe(harness.StartRefreshTimer(), _callTimeout, token).ConfigureAwait(false);
		report.Log($"StartRefreshTimer() with the timer already running {restartOutcome}");

		report.Expect(
			"StartRefreshTimer() replaces a running timer within 1 second",
			DashboardHarness.Finished,
			restartOutcome,
			restartOutcome is DashboardHarness.Finished,
			restartOutcome.StartsWith("still running", StringComparison.Ordinal) ? _deadlockHint : "StartRefreshTimer() has to dispose the running timer and create a new one without throwing.");

		if (restartOutcome is not DashboardHarness.Finished)
		{
			harness.LeaveRunning();
			return;
		}

		// Part 4: this step takes the semaphore itself, then calls each timer method. A guarded method has to wait until the step lets go.
		var startWhileHeld = await CallWhileHoldingSemaphores(semaphores, harness.StartRefreshTimer, token).ConfigureAwait(false);
		report.Log($"While this step held the semaphore, StartRefreshTimer() {startWhileHeld}");
		report.Expect("StartRefreshTimer() waits for the semaphore before it touches _refreshTimer", _waited, startWhileHeld, startWhileHeld is _waited, WaitHint("StartRefreshTimer()", startWhileHeld));

		var stopWhileHeld = await CallWhileHoldingSemaphores(semaphores, harness.StopRefreshTimer, token).ConfigureAwait(false);
		report.Log($"While this step held the semaphore, StopRefreshTimer() {stopWhileHeld}");
		report.Expect("StopRefreshTimer() waits for the semaphore before it touches _refreshTimer", _waited, stopWhileHeld, stopWhileHeld is _waited, WaitHint("StopRefreshTimer()", stopWhileHeld));

		if (startWhileHeld is not _waited || stopWhileHeld is not _waited)
		{
			// A method that did not wait ran on its own, so nothing leaked. One that threw or is still waiting left the timer in an unknown state.
			if (startWhileHeld is not (_waited or _didNotWait or _blocked) || stopWhileHeld is not (_waited or _didNotWait or _blocked))
			{
				harness.LeaveRunning();
			}

			return;
		}

		// Part 5: both methods are guarded, so now they can safely run at the same time
		report.Log($"Calling StartRefreshTimer() and StopRefreshTimer() {_concurrentCalls} times at the same time");

		var calls = Enumerable.Range(0, _concurrentCalls)
			.Select(call => Task.Run(() => call % 2 is 0 ? harness.StartRefreshTimer() : harness.StopRefreshTimer(), token))
			.ToList();

		var callsOutcome = await DashboardHarness.Describe(Task.WhenAll(calls), TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
		var finishedCalls = calls.Count(static call => call.IsCompletedSuccessfully);
		var failedCalls = calls.Count(static call => call.IsFaulted || call.IsCanceled);

		report.Log($"{finishedCalls} calls finished and {failedCalls} threw");

		report.Expect(
			$"{_concurrentCalls} StartRefreshTimer() and StopRefreshTimer() calls at the same time all finish",
			$"{_concurrentCalls} of {_concurrentCalls}",
			failedCalls > 0 ? $"{finishedCalls} of {_concurrentCalls}, {failedCalls} threw" : $"{finishedCalls} of {_concurrentCalls}",
			finishedCalls is _concurrentCalls,
			callsOutcome.StartsWith("still running", StringComparison.Ordinal)
				? $"Some calls never finished. {_releaseHint}"
				: "Some calls threw. Every read and write of _refreshTimer has to happen while the semaphore is held, so no call can find a timer that another call already disposed or set to null.");

		if (finishedCalls is not _concurrentCalls)
		{
			harness.LeaveRunning();
			return;
		}

		// Part 6: one last stop, the way DisposeAsync() stops the timer when a tab closes
		var finalStopOutcome = await DashboardHarness.Describe(harness.StopRefreshTimer(), _callTimeout, token).ConfigureAwait(false);
		var timerAfterStop = harness.GetField("_refreshTimer");

		report.Expect(
			"After a final StopRefreshTimer(), _refreshTimer is null",
			"null",
			finalStopOutcome is not DashboardHarness.Finished ? $"StopRefreshTimer() {finalStopOutcome}" : timerAfterStop is null ? "null" : "a Timer",
			finalStopOutcome is DashboardHarness.Finished && timerAfterStop is null,
			"StopRefreshTimer() has to dispose the timer and set _refreshTimer back to null while it holds the semaphore.");

		if (finalStopOutcome is not DashboardHarness.Finished || timerAfterStop is not null)
		{
			harness.LeaveRunning();
			return;
		}

		// Part 7: a timer that leaked during those calls is still ticking, and every tick applies another 60 quotes
		await Task.Delay(TimeSpan.FromMilliseconds(250), token).ConfigureAwait(false);

		var countAfterStop = harness.Dashboard.RefreshCount;
		report.Log($"Waiting {_tickWait.TotalSeconds} seconds, longer than the timer's 2 second period, to see whether any timer is still running");

		await Task.Delay(_tickWait, token).ConfigureAwait(false);

		var quotesAfterStop = harness.Dashboard.RefreshCount - countAfterStop;
		report.Expect("No refresh runs after StopRefreshTimer()", "0 quotes applied", $"{quotesAfterStop:N0} quotes applied", quotesAfterStop is 0, _leakHint);

		if (quotesAfterStop is not 0)
		{
			// Disposing this page would dispose what the leaked timer's callback still reads, and its next tick would end the app
			harness.LeaveRunning();
			report.Log("Left this dashboard running instead of disposing it, because a leaked timer is still using it");
			return;
		}

		// Part 8: close the tab. DisposeAsync() runs after the dispose token is cancelled, and has to clean up the semaphore too.
		var disposeOutcome = await harness.DisposeDashboard(token).ConfigureAwait(false);
		var disposedSemaphores = semaphores.Count(IsDisposed);
		var semaphoresDisposed = disposedSemaphores == semaphores.Count;

		report.Log($"DisposeAsync() {disposeOutcome}");

		report.Expect(
			"DisposeAsync() finishes and disposes the semaphore",
			"finished, semaphore disposed",
			disposeOutcome is not DashboardHarness.Finished ? $"DisposeAsync() {disposeOutcome}" : semaphoresDisposed ? "finished, semaphore disposed" : "finished, semaphore not disposed",
			disposeOutcome is DashboardHarness.Finished && semaphoresDisposed,
			DisposeHint(disposeOutcome));
	}

	// Takes every semaphore, calls the method, and describes what it did before and after the semaphores were released
	static async Task<string> CallWhileHoldingSemaphores(IReadOnlyList<SemaphoreSlim> semaphores, Func<Task> call, CancellationToken token)
	{
		var heldSemaphores = new List<SemaphoreSlim>();
		Task<Task> invocation;
		bool returnedWhileHeld;
		bool finishedWhileHeld;

		try
		{
			foreach (var semaphore in semaphores)
			{
				if (semaphore.Wait(0, token))
					heldSemaphores.Add(semaphore);
			}

			// A thread of its own, so a method that blocks while it waits cannot block this step
			invocation = Task.Factory.StartNew(call, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);

			await Task.Delay(_holdTime, token).ConfigureAwait(false);

			returnedWhileHeld = invocation.IsCompleted;
			finishedWhileHeld = returnedWhileHeld && invocation.Result.IsCompleted;
		}
		finally
		{
			heldSemaphores.ForEach(static semaphore => semaphore.Release());
		}

		var outcome = await DashboardHarness.Describe(invocation.Unwrap(), _callTimeout, token).ConfigureAwait(false);

		if (outcome is not DashboardHarness.Finished)
			return outcome.StartsWith("still running", StringComparison.Ordinal) ? _stillWaiting : outcome;

		if (finishedWhileHeld)
			return _didNotWait;

		return returnedWhileHeld ? _waited : _blocked;
	}

	static string WaitHint(string methodName, string outcome) => outcome switch
	{
		_didNotWait => $"{methodName} read or wrote _refreshTimer while another caller held the semaphore. Wait on the semaphore before touching the timer, and release it in a finally block.",
		_blocked => $"{methodName} blocked a thread until the semaphore was free. Wait with await WaitAsync(...) instead, so the thread goes back to the pool while it waits.",
		_stillWaiting => $"{methodName} never got the semaphore after it was released. {_deadlockHint}",
		_ => $"{methodName} has to wait for the semaphore, then touch the timer without throwing.",
	};

	static string DisposeHint(string outcome) => outcome switch
	{
		DashboardHarness.Finished => "SemaphoreSlim owns a wait handle. Dispose it in DisposeAsync(), once the timer has stopped and nothing can wait on the semaphore again.",
		"threw OperationCanceledException" or "threw TaskCanceledException" => "DisposeAsync() cancels the dispose token before it stops the timer, so a StopRefreshTimer() that waits with that token throws instead of cleaning up. Cleanup still has to run.",
		"threw ObjectDisposedException" => "Something used the semaphore or the CancellationTokenSource after it was disposed. Dispose them last, after the timer has stopped.",
		_ => $"DisposeAsync() has to stop the timer and dispose what it owns. {_releaseHint}",
	};

	// SemaphoreSlim has no IsDisposed property, but every wait on a disposed one throws
	static bool IsDisposed(SemaphoreSlim semaphore)
	{
		try
		{
			if (semaphore.Wait(0))
				semaphore.Release();

			return false;
		}
		catch (ObjectDisposedException)
		{
			return true;
		}
	}
}