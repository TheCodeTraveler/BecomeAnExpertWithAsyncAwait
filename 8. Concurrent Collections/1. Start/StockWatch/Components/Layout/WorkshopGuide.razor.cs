using Microsoft.AspNetCore.Components;

namespace StockWatch.Components.Layout;

// Workshop plumbing: the guide docked beside the app. It shows your challenge, every step's result, and the step you are working on.
public partial class WorkshopGuideBase : ComponentBase, IAsyncDisposable
{
	// Selecting this shows your challenge instead of a step
	public const int ChallengeTab = 0;

	// A run that fails quickly can finish faster than anyone can see a spinner
	static readonly TimeSpan _minimumActivityIndicatorTime = TimeSpan.FromMilliseconds(600);

	readonly CancellationTokenSource _disposeCancellationTokenSource = new();

	Task _polling = Task.CompletedTask;
	bool _isStartingCheck;
	int? _startingRunStepNumber;

	[Inject]
	public required StepVerifier Verifier { get; init; }

	[Inject]
	public required ILogger<WorkshopGuideBase> Logger { get; init; }

	public int SelectedTab { get; private set; } = ChallengeTab;

	public bool IsCollapsed { get; private set; }

	public string? ErrorMessage { get; private set; }

	public int PassedStepCount => Verifier.Steps.Count(step => Verifier.GetState(step.Number).Status is StepStatus.Passed);

	public bool IsCheckingSteps => _isStartingCheck || Verifier.IsCheckingAllSteps;

	// The first step that does not pass yet, which is the one to work on next
	public WorkshopStep? StoppedAtStep => Verifier.Steps.FirstOrDefault(step => Verifier.GetState(step.Number).Status is not StepStatus.Passed);

	// The last step the check reaches: the first one that did not pass, or the last step when they all passed
	public WorkshopStep LastCheckedStep => StoppedAtStep ?? Verifier.Steps[^1];

	public WorkshopStep? SelectedStep => Verifier.FindStep(SelectedTab);

	// The run lives in the verifier, not in this guide, so a step that is still running shows up again after a reload
	public ActiveStepRun? SelectedRun => Verifier.ActiveRun is { } run && run.Step.Number == SelectedTab ? run : null;

	public bool IsSelectedStepRunning => _startingRunStepNumber == SelectedTab || SelectedRun is not null;

	public async ValueTask DisposeAsync()
	{
		// Only stops refreshing the guide. A step that is running keeps running.
		await _disposeCancellationTokenSource.CancelAsync().ConfigureAwait(false);

		await _polling.ConfigureAwait(false);

		_disposeCancellationTokenSource.Dispose();

		GC.SuppressFinalize(this);
	}

	protected override void OnInitialized()
	{
		_polling = PollVerifier(_disposeCancellationTokenSource.Token);
	}

	protected void SelectTab(int tab)
	{
		SelectedTab = tab;
		IsCollapsed = false;
		ErrorMessage = null;
	}

	protected void ToggleCollapsed() => IsCollapsed = !IsCollapsed;

	protected async Task CheckAllStepsAsync()
	{
		var token = _disposeCancellationTokenSource.Token;
		string? errorMessage = null;

		ErrorMessage = null;
		_isStartingCheck = true;

		// Show the activity indicator before the check starts
		await InvokeAsync(StateHasChanged).ConfigureAwait(false);

		try
		{
			// Keep the indicator on screen long enough to notice, even when Step 1 fails quickly
			await Task.WhenAll(Verifier.CheckAllSteps(), Task.Delay(_minimumActivityIndicatorTime, token)).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			// The guide was closed while the steps were being checked
			return;
		}
		catch (Exception e)
		{
			Logger.LogError(e, "Checking every step failed");

			errorMessage = "Checking the steps failed. The terminal running the app has the details.";
		}

		// The continuation is off Blazor's renderer, so every component state change goes back through it
		await InvokeAsync(() =>
		{
			ErrorMessage = errorMessage;
			_isStartingCheck = false;

			StateHasChanged();
		}).ConfigureAwait(false);
	}

	protected async Task RunSelectedStepAsync()
	{
		if (SelectedStep is not { } step)
			return;

		var token = _disposeCancellationTokenSource.Token;
		string? errorMessage = null;

		ErrorMessage = null;
		_startingRunStepNumber = step.Number;

		// Show the activity indicator before the run starts
		await InvokeAsync(StateHasChanged).ConfigureAwait(false);

		try
		{
			// Keep the indicator on screen long enough to notice, even when the step fails quickly
			await Task.WhenAll(Verifier.RunStep(step), Task.Delay(_minimumActivityIndicatorTime, token)).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			// The guide was closed while the step was running
			return;
		}
		catch (Exception e)
		{
			Logger.LogError(e, "Running Step {StepNumber} failed", step.Number);

			errorMessage = "The step could not be run. The terminal running the app has the details.";
		}

		// The continuation is off Blazor's renderer, so every component state change goes back through it
		await InvokeAsync(() =>
		{
			ErrorMessage = errorMessage;
			_startingRunStepNumber = null;

			StateHasChanged();
		}).ConfigureAwait(false);
	}

	protected void StopRun() => Verifier.StopRun();

	// Re-renders while the verifier is busy, plus once more when it finishes, so the live log and results appear without a refresh
	async Task PollVerifier(CancellationToken token)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
		var wasBusy = true;

		try
		{
			while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
			{
				var isBusy = Verifier.IsBusy;

				if (isBusy || wasBusy)
					await InvokeAsync(StateHasChanged).ConfigureAwait(false);

				wasBusy = isBusy;
			}
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			// Expected when the guide is disposed
		}
	}
}