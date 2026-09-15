using Microsoft.AspNetCore.Components;

namespace CoffeeShop.Components.Pages;

public partial class StepPageBase : ComponentBase, IAsyncDisposable
{
	// A run that stops at the first stub finishes in about a millisecond, faster than anyone can see a spinner
	static readonly TimeSpan _minimumActivityIndicatorTime = TimeSpan.FromMilliseconds(600);

	readonly CancellationTokenSource _disposeCancellationTokenSource = new();

	Task _polling = Task.CompletedTask;
	bool _isStartingRun;

	[Parameter]
	public int Number { get; set; }

	[Inject]
	public required StepVerifier Verifier { get; init; }

	[Inject]
	public required ILogger<StepPageBase> Logger { get; init; }

	public WorkshopStep? Step { get; private set; }

	public bool IsBaristaAutomatic { get; set; } = true;

	public string? ErrorMessage { get; private set; }

	// The full exception behind ErrorMessage, when there is one
	public string? ErrorDetails { get; private set; }

	public StepState State => Verifier.GetState(Number);

	// The run lives in the verifier, not in this page, so leaving and coming back still shows it
	public ActiveStepRun? RunOnThisPage => Verifier.ActiveRun is { } run && run.Step.Number == Number ? run : null;

	public bool IsRunning => _isStartingRun || RunOnThisPage is not null;

	public bool IsUnlocked => Verifier.IsUnlocked(Number);

	public WorkshopStep? NextStep => Verifier.FindStep(Number + 1);

	public async ValueTask DisposeAsync()
	{
		// Only stops refreshing this page. A step that is running keeps running.
		await _disposeCancellationTokenSource.CancelAsync().ConfigureAwait(false);

		await _polling.ConfigureAwait(false);

		_disposeCancellationTokenSource.Dispose();

		GC.SuppressFinalize(this);
	}

	protected override void OnInitialized()
	{
		_polling = PollVerifier(_disposeCancellationTokenSource.Token);
	}

	// Blazor reuses this page when navigating from one step to another, so reset per-step state whenever Number changes
	protected override void OnParametersSet()
	{
		if (Step?.Number == Number)
			return;

		Step = Verifier.FindStep(Number);
		IsBaristaAutomatic = true;
		ErrorMessage = null;
		ErrorDetails = null;
	}

	protected async Task RunStepAsync()
	{
		if (Step is null)
			return;

		var token = _disposeCancellationTokenSource.Token;
		string? errorMessage = null;
		string? errorDetails = null;

		ErrorMessage = null;
		ErrorDetails = null;
		_isStartingRun = true;

		// Show the activity indicator before the run starts
		await InvokeAsync(StateHasChanged).ConfigureAwait(false);

		try
		{
			// Keep the indicator on screen long enough to notice, even when the step fails immediately
			await Task.WhenAll(Verifier.RunStep(Step, IsBaristaAutomatic), Task.Delay(_minimumActivityIndicatorTime, token)).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			// The page was closed while the step was running
			return;
		}
		catch (Exception e)
		{
			Logger.LogError(e, "Running Step {StepNumber} failed", Number);

			errorMessage = "The step could not be run:";
			errorDetails = e.ToString();
		}

		// The continuation is off Blazor's renderer, so every component state change goes back through it
		await InvokeAsync(() =>
		{
			ErrorMessage = errorMessage;
			ErrorDetails = errorDetails;
			_isStartingRun = false;

			StateHasChanged();
		}).ConfigureAwait(false);
	}

	protected void StopRun() => Verifier.StopRun();

	protected async Task PressBaristaButtonAsync(BaristaAction action)
	{
		ErrorMessage = null;
		ErrorDetails = null;

		try
		{
			await Verifier.PressBaristaButton(action).ConfigureAwait(false);
		}
		catch (Exception e)
		{
			Logger.LogError(e, "Pressing {BaristaAction} failed", action);

			await InvokeAsync(() =>
			{
				ErrorMessage = "The espresso machine did not respond:";
				ErrorDetails = e.ToString();
			}).ConfigureAwait(false);
		}

		await InvokeAsync(StateHasChanged).ConfigureAwait(false);
	}

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
			// Expected when the page is disposed
		}
	}
}